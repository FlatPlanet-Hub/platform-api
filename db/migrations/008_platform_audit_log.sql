CREATE TABLE IF NOT EXISTS platform.audit_log (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_id    UUID        NOT NULL,
    actor_email TEXT        NOT NULL,
    action      TEXT        NOT NULL,   -- 'project.create', 'project.deactivate', 'token.create', 'token.revoke'
    target_type TEXT        NOT NULL,
    target_id   UUID,
    details     JSONB,
    ip_address  TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_audit_log_actor  ON platform.audit_log(actor_id);
CREATE INDEX IF NOT EXISTS idx_audit_log_time   ON platform.audit_log(created_at DESC);

REVOKE UPDATE, DELETE ON platform.audit_log FROM PUBLIC;
