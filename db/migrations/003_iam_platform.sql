-- ============================================================
-- Migration 003: IAM Platform (Feature 6 — Core Layers 1-6)
-- ============================================================
-- Transforms the existing platform schema into a centralized
-- Identity & Access Management system.
-- Run AFTER 001_platform_schema.sql and 002_admin_dashboard.sql.
-- ============================================================

BEGIN;

-- --------------------------------------------------------
-- Layer 1: Registry
-- --------------------------------------------------------

CREATE TABLE IF NOT EXISTS platform.companies (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL,
    slug        TEXT UNIQUE NOT NULL,
    country_code TEXT,
    status      TEXT NOT NULL DEFAULT 'active',
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS platform.apps (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    company_id  UUID NOT NULL REFERENCES platform.companies(id),
    name        TEXT NOT NULL,
    slug        TEXT UNIQUE NOT NULL,
    base_url    TEXT,
    schema_name TEXT,
    status      TEXT NOT NULL DEFAULT 'active',
    registered_by UUID,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS platform.resource_types (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT UNIQUE NOT NULL,
    description TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Seed default resource types
INSERT INTO platform.resource_types (name, description) VALUES
    ('page', 'Application page or view'),
    ('section', 'Section within a page'),
    ('panel', 'UI panel or widget'),
    ('api_endpoint', 'API endpoint')
ON CONFLICT (name) DO NOTHING;

CREATE TABLE IF NOT EXISTS platform.resources (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id           UUID NOT NULL REFERENCES platform.apps(id) ON DELETE CASCADE,
    resource_type_id UUID NOT NULL REFERENCES platform.resource_types(id),
    name             TEXT NOT NULL,
    identifier       TEXT NOT NULL,
    parent_id        UUID REFERENCES platform.resources(id),
    status           TEXT NOT NULL DEFAULT 'active',
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(app_id, identifier)
);

-- --------------------------------------------------------
-- Layer 2: Identity + Access
-- --------------------------------------------------------

-- Modify users table
ALTER TABLE platform.users
    ADD COLUMN IF NOT EXISTS full_name      TEXT,
    ADD COLUMN IF NOT EXISTS role_title     TEXT,
    ADD COLUMN IF NOT EXISTS company_id     UUID REFERENCES platform.companies(id),
    ADD COLUMN IF NOT EXISTS status         TEXT NOT NULL DEFAULT 'active',
    ADD COLUMN IF NOT EXISTS password_hash  TEXT;

-- Backfill full_name from first_name + last_name
UPDATE platform.users
SET full_name = TRIM(COALESCE(first_name, '') || ' ' || COALESCE(last_name, ''))
WHERE full_name IS NULL AND (first_name IS NOT NULL OR last_name IS NOT NULL);

-- Modify roles table
ALTER TABLE platform.roles
    ADD COLUMN IF NOT EXISTS app_id           UUID REFERENCES platform.apps(id),
    ADD COLUMN IF NOT EXISTS is_platform_role BOOLEAN NOT NULL DEFAULT false;

-- Mark existing system roles as platform roles
UPDATE platform.roles SET is_platform_role = true WHERE is_system = true;

-- Add unique constraint for role names scoped to app
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_roles_app_name') THEN
        ALTER TABLE platform.roles ADD CONSTRAINT uq_roles_app_name UNIQUE (app_id, name);
    END IF;
END $$;

-- User-App-Role assignments (replaces project_members + project_roles)
CREATE TABLE IF NOT EXISTS platform.user_app_roles (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    app_id      UUID NOT NULL REFERENCES platform.apps(id) ON DELETE CASCADE,
    role_id     UUID NOT NULL REFERENCES platform.roles(id) ON DELETE CASCADE,
    granted_by  UUID REFERENCES platform.users(id),
    granted_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at  TIMESTAMPTZ,
    status      TEXT NOT NULL DEFAULT 'active',
    UNIQUE(user_id, app_id, role_id)
);

-- --------------------------------------------------------
-- Layer 3: Permissions
-- --------------------------------------------------------

-- Modify permissions table
ALTER TABLE platform.permissions
    ADD COLUMN IF NOT EXISTS app_id UUID REFERENCES platform.apps(id);

-- Add unique constraint for permission names scoped to app
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_permissions_app_name') THEN
        ALTER TABLE platform.permissions ADD CONSTRAINT uq_permissions_app_name UNIQUE (app_id, name);
    END IF;
END $$;

-- Role-Permission mapping
CREATE TABLE IF NOT EXISTS platform.role_permissions (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_id       UUID NOT NULL REFERENCES platform.roles(id) ON DELETE CASCADE,
    permission_id UUID NOT NULL REFERENCES platform.permissions(id) ON DELETE CASCADE,
    granted_by    UUID REFERENCES platform.users(id),
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(role_id, permission_id)
);

-- --------------------------------------------------------
-- Layer 4: Policy
-- --------------------------------------------------------

CREATE TABLE IF NOT EXISTS platform.resource_policies (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    resource_id      UUID NOT NULL REFERENCES platform.resources(id) ON DELETE CASCADE,
    policy_key       TEXT NOT NULL,
    policy_value     JSONB NOT NULL,
    created_by       UUID REFERENCES platform.users(id),
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(resource_id, policy_key)
);

CREATE TABLE IF NOT EXISTS platform.platform_config (
    config_key   TEXT PRIMARY KEY,
    config_value JSONB NOT NULL,
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by   UUID REFERENCES platform.users(id)
);

-- Seed default platform config
INSERT INTO platform.platform_config (config_key, config_value) VALUES
    ('jwt_expiry_minutes', '60'),
    ('refresh_token_days', '7'),
    ('api_token_max_days', '365'),
    ('session_idle_timeout_minutes', '30'),
    ('max_login_attempts', '5'),
    ('lockout_duration_minutes', '15')
ON CONFLICT (config_key) DO NOTHING;

-- --------------------------------------------------------
-- Layer 5: Sessions + Tokens
-- --------------------------------------------------------

CREATE TABLE IF NOT EXISTS platform.sessions (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    app_id      UUID REFERENCES platform.apps(id),
    ip_address  INET,
    user_agent  TEXT,
    started_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_active_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at  TIMESTAMPTZ NOT NULL,
    is_active   BOOLEAN NOT NULL DEFAULT true,
    ended_reason TEXT
);

-- Modify refresh_tokens
ALTER TABLE platform.refresh_tokens
    ADD COLUMN IF NOT EXISTS session_id UUID REFERENCES platform.sessions(id);

-- API tokens (replaces claude_tokens)
CREATE TABLE IF NOT EXISTS platform.api_tokens (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    app_id      UUID REFERENCES platform.apps(id),
    name        TEXT NOT NULL,
    token_hash  TEXT UNIQUE NOT NULL,
    permissions TEXT[] NOT NULL DEFAULT '{}',
    expires_at  TIMESTAMPTZ NOT NULL,
    revoked     BOOLEAN NOT NULL DEFAULT false,
    revoked_reason TEXT,
    last_used_at TIMESTAMPTZ,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- --------------------------------------------------------
-- Layer 6: OAuth
-- --------------------------------------------------------

CREATE TABLE IF NOT EXISTS platform.oauth_providers (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name          TEXT UNIQUE NOT NULL,
    client_id     TEXT NOT NULL DEFAULT '',
    client_secret_encrypted TEXT NOT NULL DEFAULT '',
    is_enabled    BOOLEAN NOT NULL DEFAULT true,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Seed GitHub provider
INSERT INTO platform.oauth_providers (name)
VALUES ('github')
ON CONFLICT (name) DO NOTHING;

CREATE TABLE IF NOT EXISTS platform.user_oauth_links (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id             UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    provider_id         UUID NOT NULL REFERENCES platform.oauth_providers(id),
    provider_user_id    TEXT NOT NULL,
    provider_username   TEXT,
    provider_email      TEXT,
    provider_avatar_url TEXT,
    access_token_encrypted TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(provider_id, provider_user_id),
    UNIQUE(user_id, provider_id)
);

-- --------------------------------------------------------
-- Modify projects table
-- --------------------------------------------------------

ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS app_id UUID REFERENCES platform.apps(id);

-- --------------------------------------------------------
-- Indexes
-- --------------------------------------------------------

CREATE INDEX IF NOT EXISTS idx_apps_company_id         ON platform.apps(company_id);
CREATE INDEX IF NOT EXISTS idx_apps_slug               ON platform.apps(slug);
CREATE INDEX IF NOT EXISTS idx_resources_app_id         ON platform.resources(app_id);
CREATE INDEX IF NOT EXISTS idx_resources_type_id        ON platform.resources(resource_type_id);
CREATE INDEX IF NOT EXISTS idx_user_app_roles_user_id   ON platform.user_app_roles(user_id);
CREATE INDEX IF NOT EXISTS idx_user_app_roles_app_id    ON platform.user_app_roles(app_id);
CREATE INDEX IF NOT EXISTS idx_user_app_roles_role_id   ON platform.user_app_roles(role_id);
CREATE INDEX IF NOT EXISTS idx_role_permissions_role_id  ON platform.role_permissions(role_id);
CREATE INDEX IF NOT EXISTS idx_role_permissions_perm_id  ON platform.role_permissions(permission_id);
CREATE INDEX IF NOT EXISTS idx_resource_policies_res_id  ON platform.resource_policies(resource_id);
CREATE INDEX IF NOT EXISTS idx_sessions_user_id         ON platform.sessions(user_id);
CREATE INDEX IF NOT EXISTS idx_sessions_active          ON platform.sessions(user_id, is_active) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_api_tokens_user_id       ON platform.api_tokens(user_id);
CREATE INDEX IF NOT EXISTS idx_api_tokens_app_id        ON platform.api_tokens(app_id);
CREATE INDEX IF NOT EXISTS idx_api_tokens_hash          ON platform.api_tokens(token_hash);
CREATE INDEX IF NOT EXISTS idx_user_oauth_links_user_id ON platform.user_oauth_links(user_id);
CREATE INDEX IF NOT EXISTS idx_user_oauth_links_provider ON platform.user_oauth_links(provider_id, provider_user_id);

COMMIT;
