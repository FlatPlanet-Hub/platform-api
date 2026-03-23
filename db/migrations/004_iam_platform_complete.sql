-- ============================================================
-- Migration 004: IAM Platform Complete (Feature 6 — Layers 6-9)
-- ============================================================
-- Adds MFA, Verification, Audit, and Compliance layers.
-- Run AFTER 003_iam_platform.sql.
-- ============================================================

BEGIN;

-- --------------------------------------------------------
-- Layer 6: MFA (continued from Layer 6 in 003)
-- --------------------------------------------------------

CREATE TABLE IF NOT EXISTS platform.user_mfa (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id             UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    method              TEXT NOT NULL,               -- 'totp', 'sms', 'email'
    secret_encrypted    TEXT,                        -- AES-256 encrypted TOTP secret
    phone_number        TEXT,                        -- for SMS method
    is_enabled          BOOLEAN NOT NULL DEFAULT false,
    is_verified         BOOLEAN NOT NULL DEFAULT false,
    backup_codes_hash   TEXT[],
    enrolled_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    verified_at         TIMESTAMPTZ,
    UNIQUE(user_id, method)
);

CREATE INDEX IF NOT EXISTS idx_user_mfa_user_id ON platform.user_mfa(user_id);

-- --------------------------------------------------------
-- Layer 7: Verification
-- --------------------------------------------------------

CREATE TABLE IF NOT EXISTS platform.verification_events (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    verified_user_id    UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    verified_by_user_id UUID NOT NULL REFERENCES platform.users(id),
    method              TEXT NOT NULL,    -- 'video_call', 'in_person', 'document_check', 'manager_vouch'
    outcome             TEXT NOT NULL,    -- 'verified', 'failed', 'inconclusive'
    recording_reference TEXT,
    notes               TEXT,
    verified_at         TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_verification_events_user ON platform.verification_events(verified_user_id);

-- --------------------------------------------------------
-- Layer 8: Audit
-- --------------------------------------------------------

CREATE TABLE IF NOT EXISTS platform.auth_audit_log (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID REFERENCES platform.users(id),
    app_id      UUID REFERENCES platform.apps(id),
    event_type  TEXT NOT NULL,
    ip_address  TEXT,
    user_agent  TEXT,
    details     JSONB,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Event types:
-- login_success, login_failure, logout, token_refresh, token_revoke,
-- mfa_enrol, mfa_verify, mfa_failure,
-- session_start, session_end, session_idle_timeout, session_absolute_timeout,
-- password_change, password_reset_request, password_reset_complete,
-- account_locked, account_unlocked,
-- role_granted, role_revoked, role_expired,
-- api_token_created, api_token_revoked,
-- oauth_link, oauth_unlink,
-- user_created, user_deactivated, user_reactivated

CREATE INDEX IF NOT EXISTS idx_auth_audit_user    ON platform.auth_audit_log(user_id);
CREATE INDEX IF NOT EXISTS idx_auth_audit_app     ON platform.auth_audit_log(app_id);
CREATE INDEX IF NOT EXISTS idx_auth_audit_type    ON platform.auth_audit_log(event_type);
CREATE INDEX IF NOT EXISTS idx_auth_audit_created ON platform.auth_audit_log(created_at);

CREATE TABLE IF NOT EXISTS platform.attendance_events (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         UUID NOT NULL REFERENCES platform.users(id),
    event_type      TEXT NOT NULL,   -- 'clock_in', 'clock_out', 'break_start', 'break_end'
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT now(),
    date_sydney     DATE NOT NULL,
    ip_address      TEXT,
    notes           TEXT
);

CREATE INDEX IF NOT EXISTS idx_attendance_user ON platform.attendance_events(user_id);
CREATE INDEX IF NOT EXISTS idx_attendance_date ON platform.attendance_events(date_sydney);

-- --------------------------------------------------------
-- Layer 9: Data Protection + Compliance
-- --------------------------------------------------------

CREATE TABLE IF NOT EXISTS platform.data_classification (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    resource_id     UUID NOT NULL REFERENCES platform.resources(id) ON DELETE CASCADE,
    classification  TEXT NOT NULL,   -- 'public', 'internal', 'confidential', 'restricted'
    handling_notes  TEXT,
    classified_by   UUID REFERENCES platform.users(id),
    classified_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(resource_id)
);

CREATE TABLE IF NOT EXISTS platform.consent_records (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         UUID NOT NULL REFERENCES platform.users(id) ON DELETE CASCADE,
    consent_type    TEXT NOT NULL,   -- 'terms_of_service', 'privacy_policy', 'data_processing', 'marketing'
    version         TEXT NOT NULL,
    consented       BOOLEAN NOT NULL,
    ip_address      TEXT,
    consented_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    withdrawn_at    TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_consent_user ON platform.consent_records(user_id);

CREATE TABLE IF NOT EXISTS platform.incident_log (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    reported_by           UUID REFERENCES platform.users(id),
    severity              TEXT NOT NULL,   -- 'low', 'medium', 'high', 'critical'
    title                 TEXT NOT NULL,
    description           TEXT NOT NULL,
    affected_app_id       UUID REFERENCES platform.apps(id),
    affected_users_count  INTEGER,
    status                TEXT NOT NULL DEFAULT 'open',  -- 'open', 'investigating', 'resolved', 'closed'
    resolution            TEXT,
    reported_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    resolved_at           TIMESTAMPTZ,
    closed_at             TIMESTAMPTZ
);

COMMIT;
