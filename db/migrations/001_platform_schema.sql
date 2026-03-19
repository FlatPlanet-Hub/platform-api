-- ============================================================
-- Migration 001 — Platform schema
-- Run in Supabase SQL Editor (as postgres superuser)
-- ============================================================

CREATE SCHEMA IF NOT EXISTS platform;

-- ─── USERS ───────────────────────────────────────────────────────────────────
CREATE TABLE platform.users (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    github_id           BIGINT UNIQUE NOT NULL,
    github_username     TEXT NOT NULL,
    email               TEXT,
    display_name        TEXT NOT NULL,
    avatar_url          TEXT,
    github_access_token TEXT,          -- AES-256 encrypted
    is_active           BOOLEAN DEFAULT true,
    created_at          TIMESTAMPTZ DEFAULT now(),
    updated_at          TIMESTAMPTZ DEFAULT now()
);

-- ─── ROLES (system-wide) ─────────────────────────────────────────────────────
CREATE TABLE platform.roles (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT UNIQUE NOT NULL,
    description TEXT,
    is_system   BOOLEAN DEFAULT false,
    created_at  TIMESTAMPTZ DEFAULT now()
);

INSERT INTO platform.roles (name, description, is_system) VALUES
    ('platform_admin', 'Full platform access, can manage all users and projects', true),
    ('user',           'Standard user, can create projects and manage their own',  true);

-- ─── USER ROLES ──────────────────────────────────────────────────────────────
CREATE TABLE platform.user_roles (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    role_id     UUID NOT NULL REFERENCES platform.roles(id) ON DELETE CASCADE,
    assigned_by UUID REFERENCES platform.users(id),
    assigned_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(user_id, role_id)
);

-- ─── PROJECTS ────────────────────────────────────────────────────────────────
CREATE TABLE platform.projects (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL,
    description TEXT,
    schema_name TEXT UNIQUE NOT NULL,
    owner_id    UUID NOT NULL REFERENCES platform.users(id),
    github_repo TEXT,
    is_active   BOOLEAN DEFAULT true,
    created_at  TIMESTAMPTZ DEFAULT now(),
    updated_at  TIMESTAMPTZ DEFAULT now()
);

-- ─── PROJECT ROLES ───────────────────────────────────────────────────────────
CREATE TABLE platform.project_roles (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES platform.projects(id) ON DELETE CASCADE,
    name       TEXT NOT NULL,
    permissions TEXT[] NOT NULL,
    is_default  BOOLEAN DEFAULT false,
    created_at  TIMESTAMPTZ DEFAULT now(),
    UNIQUE(project_id, name)
);

-- ─── PROJECT MEMBERS ─────────────────────────────────────────────────────────
CREATE TABLE platform.project_members (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id      UUID NOT NULL REFERENCES platform.projects(id) ON DELETE CASCADE,
    user_id         UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    project_role_id UUID NOT NULL REFERENCES platform.project_roles(id),
    invited_by      UUID REFERENCES platform.users(id),
    status          TEXT DEFAULT 'active',
    joined_at       TIMESTAMPTZ DEFAULT now(),
    UNIQUE(project_id, user_id)
);

-- ─── REFRESH TOKENS ──────────────────────────────────────────────────────────
CREATE TABLE platform.refresh_tokens (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    token_hash TEXT UNIQUE NOT NULL,  -- SHA-256 hex of raw token
    expires_at TIMESTAMPTZ NOT NULL,
    revoked    BOOLEAN DEFAULT false,
    created_at TIMESTAMPTZ DEFAULT now()
);

-- ─── CLAUDE TOKENS ───────────────────────────────────────────────────────────
CREATE TABLE platform.claude_tokens (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES platform.projects(id) ON DELETE CASCADE,
    token_hash TEXT UNIQUE NOT NULL,  -- SHA-256 hex of raw JWT
    expires_at TIMESTAMPTZ NOT NULL,
    revoked    BOOLEAN DEFAULT false,
    created_at TIMESTAMPTZ DEFAULT now()
);

-- ─── AUDIT LOG ───────────────────────────────────────────────────────────────
CREATE TABLE platform.audit_log (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID REFERENCES platform.users(id),
    project_id UUID REFERENCES platform.projects(id),
    action     TEXT NOT NULL,
    resource   TEXT,
    details    JSONB,
    ip_address TEXT,
    created_at TIMESTAMPTZ DEFAULT now()
);

-- ─── INDEXES ─────────────────────────────────────────────────────────────────
CREATE INDEX idx_users_github_id           ON platform.users(github_id);
CREATE INDEX idx_user_roles_user           ON platform.user_roles(user_id);
CREATE INDEX idx_project_members_user      ON platform.project_members(user_id);
CREATE INDEX idx_project_members_project   ON platform.project_members(project_id);
CREATE INDEX idx_refresh_tokens_user       ON platform.refresh_tokens(user_id);
CREATE INDEX idx_claude_tokens_user        ON platform.claude_tokens(user_id);
CREATE INDEX idx_claude_tokens_project     ON platform.claude_tokens(project_id);
CREATE INDEX idx_audit_log_user            ON platform.audit_log(user_id);
CREATE INDEX idx_audit_log_project         ON platform.audit_log(project_id);
CREATE INDEX idx_audit_log_created         ON platform.audit_log(created_at);
