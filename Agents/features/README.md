# Features — HubApi ISO 27001 Compliance

## Architecture Principle

**HubApi does not handle MFA.** MFA is enforced by the Security Platform at login time.
By the time HubApi receives a JWT, the user has already passed all SP-required checks.
HubApi's only ISO 27001 responsibility is logging its own admin write operations.

---

## Feature Index

| Feature | File | Depends on | Status |
|---|---|---|---|
| FEAT-04 | [feat-04-hub-audit-log.md](feat-04-hub-audit-log.md) | FEAT-02 migrations run in SP | ⏳ Pending |

---

## What HubApi Does and Does Not Do

| Concern | HubApi? | Who handles it |
|---|---|---|
| MFA enrollment | ❌ No | SP — `POST /api/v1/mfa/enroll` |
| MFA at login | ❌ No | SP — login gate in `AuthService` |
| JWT validation | ✅ Yes | Already working — no changes |
| Admin audit log | ✅ Yes | FEAT-04 |
| Identity verification | ❌ No | SP — `GET /api/v1/identity/verification/status` |

---

## Order

1. Run FEAT-02 migrations in SP first (V7–V10 on the SP database)
2. Run the FEAT-04 migration `008_platform_audit_log.sql` on HubApi's database
3. Build FEAT-04 — no dependency on any SP feature code, only the migration above

---

## Notes for HubApi Coder

**Connection pool** — HubApi's connection string already has `No Reset On Close=true;Maximum Pool Size=5` from a previous fix. Do not change it.

**Phone numbers** — HubApi never stores phone numbers. MFA enrollment is done entirely in SP. If the HubApi frontend needs to show a masked phone number, it calls `GET /api/v1/identity/verification/status` on SP using the user's JWT — HubApi does not proxy or cache this data.

---

## Future Apps

When a new app is registered in SP:
- It uses the same SP JWT — MFA enforcement is already there
- It builds its own audit log if needed (same pattern as FEAT-04)
- Zero changes to HubApi
