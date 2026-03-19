-- ============================================================
-- Migration 002 — Admin Dashboard (Feature 3)
-- Run in Supabase SQL Editor (as postgres superuser)
-- ============================================================

-- ─── ALTER platform.users ────────────────────────────────────────────────────
ALTER TABLE platform.users
    DROP COLUMN IF EXISTS display_name,
    ADD COLUMN IF NOT EXISTS first_name   TEXT,
    ADD COLUMN IF NOT EXISTS last_name    TEXT,
    ADD COLUMN IF NOT EXISTS onboarded_by UUID REFERENCES platform.users(id);

-- ─── CUSTOM ROLES (admin-defined, system-level) ───────────────────────────────
CREATE TABLE IF NOT EXISTS platform.custom_roles (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT UNIQUE NOT NULL,
    description TEXT,
    permissions TEXT[] NOT NULL,
    created_by  UUID REFERENCES platform.users(id),
    is_active   BOOLEAN DEFAULT true,
    created_at  TIMESTAMPTZ DEFAULT now(),
    updated_at  TIMESTAMPTZ DEFAULT now()
);

-- ─── USER CUSTOM ROLES ────────────────────────────────────────────────────────
-- Assigns admin-defined custom roles to users (separate from system platform.roles)
CREATE TABLE IF NOT EXISTS platform.user_custom_roles (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id        UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    custom_role_id UUID NOT NULL REFERENCES platform.custom_roles(id) ON DELETE CASCADE,
    assigned_by    UUID REFERENCES platform.users(id),
    assigned_at    TIMESTAMPTZ DEFAULT now(),
    UNIQUE(user_id, custom_role_id)
);

-- ─── AVAILABLE PERMISSIONS (reference table) ──────────────────────────────────
CREATE TABLE IF NOT EXISTS platform.permissions (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT UNIQUE NOT NULL,
    description TEXT,
    category    TEXT NOT NULL
);

INSERT INTO platform.permissions (name, description, category) VALUES
    ('read',           'Query and view data',                    'data'),
    ('write',          'Insert, update, delete data',            'data'),
    ('ddl',            'Create and alter tables',                'schema'),
    ('manage_members', 'Invite and remove project members',      'project'),
    ('delete_project', 'Delete or deactivate a project',         'project'),
    ('manage_users',   'Create and edit user records',           'admin'),
    ('manage_roles',   'Create and edit roles',                  'admin'),
    ('view_audit_log', 'View audit log entries',                 'admin')
ON CONFLICT (name) DO NOTHING;

-- ─── INDEXES ─────────────────────────────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_user_custom_roles_user   ON platform.user_custom_roles(user_id);
CREATE INDEX IF NOT EXISTS idx_user_custom_roles_role   ON platform.user_custom_roles(custom_role_id);
CREATE INDEX IF NOT EXISTS idx_custom_roles_active      ON platform.custom_roles(is_active);
