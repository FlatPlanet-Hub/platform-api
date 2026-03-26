# FlatPlanet Hub — Feature 4: GitHub DATA_DICTIONARY Sync

## What This Is
After every DDL operation (create table, alter table, drop table), HubApi automatically updates `DATA_DICTIONARY.md` in the project's GitHub repo. This keeps the repo's schema documentation in sync with the actual database.

Claude Code reads this file from the repo to understand the current data model when it starts working on a project.

## What Was Removed
The following are **handled by the frontend** — not HubApi:
- Repo creation (`POST /repos`)
- File read/write (`GET/PUT /repo/files`)
- Commit operations
- Branch management
- PR operations
- Collaborator management via frontend GitHub UI

Member-level collaborator management (add/remove from repo when adding/removing project members) is handled in **Feature 3** using the GitHub service token.

---

## DATA_DICTIONARY Auto-Sync

### Trigger
Any successful DDL operation in Feature 1 triggers a sync:
- `POST /api/projects/{id}/migration/create-table`
- `PUT /api/projects/{id}/migration/alter-table`
- `DELETE /api/projects/{id}/migration/drop-table`

### Behavior
After the DDL executes successfully, HubApi:
1. Reads the current schema from `information_schema` (all tables + columns + types + relationships)
2. Generates an updated `DATA_DICTIONARY.md`
3. Commits it to the project's GitHub repo on the current default branch

**Fire-and-forget:** A GitHub failure never rolls back a successful DDL. The sync is best-effort — if GitHub is unreachable, the DDL still succeeds. The dictionary will be updated on the next DDL operation.

### DATA_DICTIONARY.md Format
```markdown
# Data Dictionary — {Project Name}

Last updated: {timestamp}

## Tables

### customers
| Column | Type | Nullable | Default | Notes |
|--------|------|----------|---------|-------|
| id | uuid | NOT NULL | gen_random_uuid() | Primary key |
| name | text | NOT NULL | | |
| email | text | NOT NULL | | |
| created_at | timestamptz | NOT NULL | now() | |

### orders
| Column | Type | Nullable | Default | Notes |
|--------|------|----------|---------|-------|
| id | uuid | NOT NULL | gen_random_uuid() | Primary key |
| customer_id | uuid | NOT NULL | | FK → customers.id |
| total | numeric | NOT NULL | | |
| created_at | timestamptz | NOT NULL | now() | |

## Relationships
- orders.customer_id → customers.id
```

---

## GitHub Service Token

Uses the `GitHub:ServiceToken` from config (same service token as Feature 3 — shared).

The service token needs `contents: write` permission on the repo to commit `DATA_DICTIONARY.md`.

---

## Initial Repo Files (on Project Creation)

When a project is registered via `POST /api/projects` (Feature 3), HubApi seeds the repo with:

| File | Purpose |
|------|---------|
| `DATA_DICTIONARY.md` | Empty template — Claude Code updates this as tables are created |
| `CLAUDE.md` (gitignored) | Listed in `.gitignore` — never committed, generated per-user in Feature 5 |

The `.gitignore` seeded at repo creation must include:
```
CLAUDE.md
.env
.env.local
```

This ensures API tokens never end up in git history.
