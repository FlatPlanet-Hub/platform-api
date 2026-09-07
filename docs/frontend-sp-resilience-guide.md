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

As of PR #39, HubApi has a **two-tier in-memory cache** on each user's SP access list:

- **Fresh tier (5 min)**: normal cache — repeat calls within 5 min skip SP entirely
- **Stale tier (2 hours)**: outage fallback — if SP becomes unreachable AND the fresh entry has expired, HubApi serves the last known value rather than failing
- **Explicit invalidation** on role changes (grant/change/revoke) — both keys cleared immediately so revocations are never delayed

What this means for the frontend:
- Brief SP outages (Azure restarts, maintenance, anything under a few minutes) are **invisible** to users already in session
- An SP outage *longer* than 2 hours will eventually start failing — at that point surface a clear "platform temporarily unavailable" message rather than looping
- New logins while SP is down are still a hard failure — the JWT is issued by SP and cannot be obtained without it

**Practical implication for retry strategy:** A 3-second retry delay (rule 2) covers brief glitches. The cache covers everything from a few seconds to a couple of hours. The frontend almost never needs to surface SP outages to the user.

---

## 7. HTTP 429 — Too Many Requests (rate limiting)

HubApi rate limits requests in three layers, all of which must pass. Ceilings are
now **differentiated by the JWT's `token_type` claim** rather than hardcoded per
project: a `user_token` (one human) gets the strict tier, while a `service_token`
or `api_token` (a backend app authenticating on behalf of many end-users) gets a
higher tier, because that single token's traffic represents an entire app's user
base, not one person. A missing or unrecognized `token_type` falls back to the
strict `user_token` tier (fail closed).

| Layer | Scope | `user_token` | `service_token` / `api_token` |
|---|---|---|---|
| 1 | per user (or IP), global | 1,000/min | 1,000/min |
| 2 | per project | 500/min | 3,000/min |
| 3 | per (project, user) — `/api/projects/{id}/query/{read,write,ddl}` | 40/min | 500/min |

No per-project configuration is needed for any of this — every FlatPlanet app
gets the appropriate ceiling automatically based on the kind of token it
authenticates with.

A 429 response looks like:
```
HTTP/1.1 429 Too Many Requests
Retry-After: 60
Content-Type: application/json

{"success": false, "message": "Too many requests for this project. Please retry after 60 seconds."}
```

**Rules for handling 429:**

- **Do not** clear tokens or redirect to login — 429 is not an auth failure
- **Do not** retry immediately — that's how you got here
- **Do** respect the `Retry-After` header — wait that many seconds before any further attempt to the same endpoint
- **Do** show a non-disruptive message to the user — something like "slow down, too many requests" or just throttle silently in the background
- **Do** consider whether your code has a bug (a `useEffect` without cleanup that keeps firing requests, a polling loop without backoff, etc.) — 429s are usually a symptom of a tight loop on the client

Detection pattern:
```js
if (res.status === 429) {
  const retryAfter = parseInt(res.headers.get('Retry-After') || '60', 10);
  // Back off — do not retry until window resets
  await new Promise(r => setTimeout(r, retryAfter * 1000));
  // Optional: surface a soft "throttled" indicator in UI
}
```

If your frontend is hitting 429 in normal use, the rate limit isn't the problem — your client code is firing too many requests.

---

## 8. The 401 loop bug — single-flight refresh and stop on failure

The biggest real-world failure mode we've seen: the frontend gets 401 on `/api/projects`, tries to refresh, fails, then **keeps retrying both endpoints in a loop instead of redirecting to login**. The browser console fills with 401s, the page shows "0 projects", the user is stuck.

This happens when several requests fire concurrently and each independently triggers refresh on 401. With no coordination:

1. Request A → 401 → triggers `refreshAccessToken()` (consumes refresh token)
2. Request B → 401 (same access token expired) → triggers `refreshAccessToken()` (refresh token now used, returns 401)
3. Request C → 401 → triggers `refreshAccessToken()` → 401 → loop

To prevent this:

### Single-flight refresh

Only ONE refresh attempt should be in flight at any time. All concurrent requests that hit a 401 should **wait for that single refresh to complete**, then retry once with the new token.

```js
let refreshPromise = null;

async function getValidAccessToken() {
  if (refreshPromise) return refreshPromise; // already refreshing — wait for it
  if (accessTokenStillValid()) return getStoredAccessToken();

  refreshPromise = doRefresh()
    .finally(() => { refreshPromise = null; });

  return refreshPromise;
}

async function doRefresh() {
  const res = await fetch('/api/v1/auth/refresh', { ... });
  if (!res.ok) {
    // Genuine session expiry — one place, no retries, no loops
    clearTokens();
    redirectToLogin();
    throw new Error('Session expired');
  }
  const tokens = await res.json();
  storeTokens(tokens.accessToken, tokens.refreshToken);
  return tokens.accessToken;
}
```

### Stop on refresh failure

After `refreshAccessToken()` returns a 401, **no further API calls should fire** until the user re-authenticates. The cleanest way:

- `clearTokens()` removes them from storage
- All API call helpers check `getStoredAccessToken()` first and abort early if missing
- `redirectToLogin()` actually navigates the page

If you see your console filling with repeated 401s on `/auth/refresh` AND `/api/projects` AND `/auth/heartbeat` all interleaving — that's this bug. Fix it before shipping.

---

## 9. Session evicted by concurrent login

A user's refresh token can become invalid *without* their session expiring naturally:

- The user logs into the hub from another browser/device — SP enforces a max concurrent session count (default 3) and evicts the oldest, invalidating its refresh token
- An admin runs an account operation that revokes sessions (password reset, profile email change, etc.)
- The user's session was server-side ended for any reason

From the frontend's perspective this looks **identical** to a normally-expired refresh token: the `/auth/refresh` call returns 401. The handling is the same — clear tokens, redirect to login. No special treatment needed beyond what rule 3 already covers.

But this is one more reason **single-flight refresh + stop-on-failure** (rule 8) matters: if a user has multiple tabs open and one tab gets their session evicted, **all** tabs need to handle it cleanly without spawning a flurry of failed refreshes.

---

## 10. Stale token on app boot

When the app loads, the first thing it should do with stored tokens is **validate they parse correctly and aren't obviously expired**, before firing any API calls.

```js
function bootCheck() {
  const access = getStoredAccessToken();
  if (!access) { redirectToLogin(); return; }

  // Decode JWT exp claim (no signature check — just shape check)
  try {
    const payload = JSON.parse(atob(access.split('.')[1]));
    if (payload.exp * 1000 < Date.now()) {
      // Token already expired before app even loaded — go straight to refresh
      return refreshAccessToken();
    }
  } catch {
    // Token is malformed — treat as missing
    clearTokens();
    redirectToLogin();
  }
}
```

This avoids the "stuck loading projects, console filling with 401s" experience after the user closes the laptop for a few hours and comes back.

---

## Summary

| Scenario | Correct response |
|---|---|
| 401 on any non-refresh endpoint | Trigger single-flight refresh, retry once. If refresh succeeds, continue. If refresh fails, log out. |
| 5xx or network error on any endpoint | Retry once after 3 seconds |
| HubApi returns 502 with `"Security Platform error"` | Transient SP outage — retry once, do not log out |
| SP outage lasts >2 hours | HubApi stale cache expires — show "platform temporarily unavailable", stop retrying |
| 401 on `POST /auth/refresh` | Clear tokens, redirect to login. No retries, no loops. |
| Heartbeat fails | Skip the tick, continue the interval |
| Token expires during SP outage | Access token is valid for 4 hours — user stays logged in |
| New login while SP is down | Hard failure — SP must be reachable to issue JWTs, no workaround |
| **HTTP 429 on any endpoint** | **Back off per `Retry-After` header. Do not log out. Investigate client code for tight loops.** |
| **Concurrent requests hit 401 at once** | **Coordinate via single-flight refresh (rule 8). Only one refresh in flight.** |
| **Session evicted (user logged in elsewhere, admin reset, etc.)** | **Looks identical to expired refresh. Same handling: clear, redirect.** |
| **App boot with stored tokens** | **Validate token shape and exp claim before firing any API call. (rule 10)** |

---

## Implementation checklist for the hub frontend

If you're auditing the hub against this guide, here's a quick checklist:

- [ ] Single-flight refresh: only one `/auth/refresh` request can be in flight at a time
- [ ] On `/auth/refresh` 401: clear tokens, redirect to login, abort any other pending requests
- [ ] On any other 401: try refresh ONCE, retry the original request ONCE on success, give up cleanly on failure
- [ ] On 502 with `"Security Platform error"` body: retry once after 3s, then surface a transient error
- [ ] On 429: read `Retry-After` header, back off, do NOT logout
- [ ] On heartbeat 401/5xx: skip tick, continue interval
- [ ] On app boot: validate stored access token's `exp` claim before making any API call
- [ ] Don't fire API requests in `useEffect` without an `AbortController` cleanup — cancelled requests show up as `BadHttpRequestException` in HubApi logs and create noise

If any of these are missing, you'll eventually see the 401-loop bug we documented in rule 8.
