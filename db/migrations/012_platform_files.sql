CREATE TABLE IF NOT EXISTS platform.files (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    business_code   TEXT NOT NULL,
    category        TEXT NOT NULL DEFAULT 'general',
    original_name   TEXT NOT NULL,
    blob_name       TEXT NOT NULL,
    content_type    TEXT NOT NULL,
    file_size_bytes BIGINT NOT NULL,
    uploaded_by     UUID NOT NULL,
    tags            TEXT[] DEFAULT '{}',
    is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at      TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_files_business_code ON platform.files(business_code);
CREATE INDEX IF NOT EXISTS idx_files_uploaded_by   ON platform.files(uploaded_by);
CREATE INDEX IF NOT EXISTS idx_files_category      ON platform.files(business_code, category) WHERE is_deleted = FALSE;
