# FlatPlanet Hub — Feature 2: Authentication via Security Platform

## What This Is
HubApi does NOT handle authentication. The Security Platform (flatplanet-security-platform) is the sole identity provider. HubApi validates Security Platform JWTs and uses the claims inside them.

Users log in at the Security Platform. The Security Platform issues a JWT. The frontend passes that JWT to HubApi on every request.

## What Was Removed
The following no longer exist in HubApi:
- GitHub OAuth flow (`GET /api/auth/oauth/github`, callback)
- JWT issuance (`JwtService.GenerateAppToken`)
- Refresh token management (`refresh_tokens` table, `/api/auth/refresh`)
- Session management (`sessions` table, `/api/auth/logout`)
- `IGitHubOAuthService`, `IUserService.UpsertFromGitHubAsync`

All of the above are now owned by the Security Platform.

---

## How Auth Works in HubApi

HubApi configures `JwtBearer` to validate tokens issued by the Security Platform:

```json
"Jwt": {
  "Issuer": "flatplanet-security",
  "Audience": "flatplanet-apps",
  "SecretKey": "same secret as Security Platform"
}
```

Every `[Authorize]` endpoint in HubApi validates the Security Platform JWT automatically. No extra code needed.

---

## API Endpoints

### Who Am I
```
GET /api/auth/me
Authorization: Bearer {security-platform-jwt}
```

Returns the authenticated user's identity and the projects they have access to. Claims are read directly from the JWT — no extra DB call needed for basic identity.

Response:
```json
{
  "success": true,
  "data": {
    "userId": "user-uuid",
    "email": "chris@example.com",
    "fullName": "Chris Moriarty",
    "companyId": "company-uuid",
    "canCreateProject": true
  }
}
```

`canCreateProject` is derived from whether the user has the `create_project` permission in the Security Platform JWT claims.

---

## JWT Claims HubApi Reads

The Security Platform JWT contains:
```json
{
  "sub": "user-uuid",
  "email": "user@example.com",
  "full_name": "Chris Moriarty",
  "company_id": "company-uuid",
  "session_id": "session-uuid",
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": ["platform_owner"]
}
```

HubApi reads:
- `sub` → user ID for all operations
- `full_name`, `email`, `company_id` → user identity
- Role claims → `platform_owner` or `app_admin` get elevated access in HubApi

---

## Two Token Types in HubApi

| Token | Source | Used for | Routes |
|-------|--------|----------|--------|
| Security Platform JWT | Security Platform login | Frontend ↔ HubApi (projects, CLAUDE.md) | All `/api/projects/*`, `/api/auth/me` |
| HubApi API Token | `GET /api/projects/{id}/claude-config` | Claude Code ↔ DB Proxy | `/api/projects/{id}/query/*`, `/api/projects/{id}/migration/*`, `/api/projects/{id}/schema/*` |

These are distinct. The Security Platform JWT is short-lived (60 min) and for user interactions. The HubApi API Token is long-lived (30 days) and only for Claude Code DB proxy calls.

---

## Configuration

```json
"Jwt": {
  "Issuer": "flatplanet-security",
  "Audience": "flatplanet-apps",
  "SecretKey": "MUST_MATCH_SECURITY_PLATFORM_SECRET"
},
"SecurityPlatform": {
  "BaseUrl": "https://security.flatplanet.com"
}
```
