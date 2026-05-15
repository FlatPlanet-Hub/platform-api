# Frontend Resilience Guide — Security Platform

> **Why this matters**: The Security Platform (SP) is the single point of authentication for the entire FlatPlanet platform. When SP restarts — due to a deployment, Azure maintenance, or any other reason — there is a brief window (under 30 seconds) where requests may fail. The frontend should handle this gracefully so users are not kicked out unnecessarily.

---

## 1. Don't treat every 401 as a logout

Not all 401 responses mean the user's session is dead.

**The rule:**

| Where the 401 came from | What it means | What to do |
|---|---|---|
| `POST /auth/refresh` | Session is genuinely expired or revoked | Clear tokens, redirect to login |
| Anywhere else | SP may be briefly unavailable, or a transient error | Show an error message, let the user retry |

**Do not** clear tokens and redirect to login on a 401 from a permission check, project load, or any non-auth endpoint. The user's token is likely still valid — SP just couldn't respond in that moment.

---

## 2. Retry once before surfacing an error

A single failed request during a brief SP restart should not end the user's session or show a hard error. Add a simple retry with a short delay on network errors and 5xx responses.

**Recommended pattern:**

```js
async function fetchWithRetry(url, options, retries = 1, delayMs = 3000) {
  try {
    const res = await fetch(url, options);
    if (!res.ok && res.status >= 500 && retries > 0) {
      await new Promise(r => setTimeout(r, delayMs));
      return fetchWithRetry(url, options, retries - 1, delayMs);
    }
    return res;
  } catch (err) {
    if (retries > 0) {
      await new Promise(r => setTimeout(r, delayMs));
      return fetchWithRetry(url, options, retries - 1, delayMs);
    }
    throw err;
  }
}
```

- Retry once on 5xx or network failure
- Wait 3 seconds before retrying — this covers most brief restart windows
- Do not retry on 4xx (except as noted in rule 1 above)

---

## 3. Refresh token flow — the one place to log out on failure

The refresh token call is the only place where a failure should result in a logout.

```js
async function refreshAccessToken() {
  const res = await fetch('/api/v1/auth/refresh', {
    method: 'POST',
    body: JSON.stringify({ refreshToken: getStoredRefreshToken() })
  });

  if (!res.ok) {
    // Genuine session expiry — clear everything and redirect
    clearTokens();
    redirectToLogin();
    return;
  }

  const { accessToken, refreshToken } = await res.json();
  storeTokens(accessToken, refreshToken);
}
```

Refresh tokens are single-use. If this call fails, the session cannot be recovered — this is the correct place to log out.

---

## 4. Heartbeat errors — do not log out

The heartbeat endpoint (`POST /api/v1/auth/heartbeat`) keeps the session alive. If it returns an error:

- **Do not** clear the interval
- **Do not** clear tokens or redirect
- Simply skip that heartbeat tick and try again on the next interval

SP being briefly unavailable should not terminate an active session.

---

## 5. HubApi returns 502 when SP is down — do not log out

When the frontend calls a HubApi endpoint (project load, member list, Claude config, etc.) and SP is unreachable, HubApi returns a `502` with this body:

```json
{ "success": false, "data": null, "error": "Security Platform error: 502 — " }
```

This is not an authentication failure. The user's JWT is still valid. The correct response is identical to rule 2: retry once after 3 seconds, then surface a transient error message if it still fails.

**Do not** interpret a `502` from a HubApi endpoint as a session expiry. Do not clear tokens or redirect to login.

Detection pattern:
```js
if (res.status === 502) {
  const body = await res.json();
  if (body?.error?.startsWith('Security Platform error')) {
    // Transient SP outage — retry, do not log out
  }
}
```

---

## 6. What the cache buys you

As of PR #38, HubApi caches each user's SP access list for **60 seconds** in-process. This means:

- Brief SP outages under 60 seconds are **invisible** to users already in session — HubApi serves from cache and never calls SP
- SP going down mid-session does not immediately break any in-flight operations
- After 60 seconds of SP being down, HubApi cache entries expire and requests will start failing

**Practical implication for retry strategy:** A 3-second retry delay (rule 2) covers brief SP restarts. For a longer SP outage (>60s), retries will keep failing — at that point surface a clear "platform temporarily unavailable" message rather than looping.

New logins while SP is down are a hard failure regardless of caching — the JWT is issued by SP and cannot be obtained without it.

---

## Summary

| Scenario | Correct response |
|---|---|
| 401 on any non-refresh endpoint | Show error, let user retry. Do not log out. |
| 5xx or network error on any endpoint | Retry once after 3 seconds |
| HubApi returns 502 with `"Security Platform error"` | Transient SP outage — retry once, do not log out |
| SP outage lasts >60s | HubApi cache expires — show "platform temporarily unavailable", stop retrying |
| 401 on `POST /auth/refresh` | Clear tokens, redirect to login |
| Heartbeat fails | Skip the tick, continue the interval |
| Token expires during SP outage | Access token is valid for 4 hours — user stays logged in |
| New login while SP is down | Hard failure — SP must be reachable to issue JWTs, no workaround |
