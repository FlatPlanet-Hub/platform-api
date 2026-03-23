# FlatPlanet.Platform — Feature 2: GitHub OAuth + JWT Token Issuance

## What This Is
The authentication flow for the current project. Users sign in via GitHub OAuth. This feature handles the OAuth handshake and issues JWT tokens that Feature 1 (Supabase Proxy API) consumes.

**All user records, roles, permissions, sessions, and audit logging are managed by Feature 6 (Flat Planet IAM).** This feature only handles the GitHub-specific OAuth flow and token generation.

## Architecture
```
User → Your App → GitHub OAuth → Feature 6 IAM API (user lookup/creation)
                                        ↓
                                  Issues JWT tokens
                                        ↓
                              Feature 1 (Proxy API) validates them
```

## How It Works

### Login Flow
1. Frontend redirects to `GET /api/auth/oauth/github` (Feature 6 endpoint)
2. Feature 6 redirects to GitHub consent screen
3. User authorizes → GitHub redirects to callback
4. Feature 6 exchanges code for GitHub access token
5. Feature 6 fetches GitHub profile (id, login, email, avatar)
6. Feature 6 looks up `user_oauth_links` by `provider_user_id`:
   - Exists → update access token, login
   - New → **reject** (user must be onboarded by admin first via Feature 3)
7. Feature 6 creates a session in `sessions` table
8. Feature 6 issues JWT + refresh token
9. Redirect to frontend with tokens

### GitHub OAuth Scopes
- `user:email` — read user's email
- `repo` — read/write repos (for Claude Code to push code)

### JWT Token Types

**App JWT** (short-lived, 60 min) — for frontend app use:
```json
{
  "sub": "user-uuid",
  "email": "user@example.com",
  "full_name": "Chris Moriarty",
  "company_id": "company-uuid",
  "apps": [
    {
      "app_id": "app-uuid",
      "app_slug": "current-project",
      "roles": ["developer"],
      "permissions": ["read", "write", "ddl"]
    }
  ],
  "token_type": "app",
  "iat": 1234567890,
  "exp": 1234571490
}
```

The JWT claims are populated by querying Feature 6 tables: `user_app_roles` → `roles` → `role_permissions` → `permissions`.

**Claude API Token** (long-lived, 30 days) — for MCP/service use:
Generated via `POST /api/auth/api-tokens` (Feature 6 endpoint). Stored in `api_tokens` table. Scoped to a single app with specific permissions.

### What This Feature Does NOT Handle
- User creation → Feature 3 (admin onboards users)
- User records, roles, permissions tables → Feature 6 (IAM)
- Session management → Feature 6
- Audit logging → Feature 6
- MFA → Feature 6

### Configuration (extends Feature 6 appsettings.json)
```json
{
  "OAuth": {
    "GitHub": {
      "ClientId": "YOUR_GITHUB_CLIENT_ID",
      "ClientSecret": "YOUR_GITHUB_CLIENT_SECRET",
      "RedirectUri": "https://your-api.com/api/auth/oauth/github/callback",
      "Scopes": "user:email,repo"
    }
  }
}
```

### Security
1. Random `state` parameter for CSRF protection on OAuth flow
2. GitHub access token encrypted (AES-256) before storing in `user_oauth_links`
3. New users cannot self-register — admin must onboard them first (Feature 3)
4. All auth events logged in `auth_audit_log` (Feature 6)
