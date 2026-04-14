-- 013: Scope platform.files by app_id
-- Files uploaded by a project are now isolated to that app.
-- Existing rows get NULL app_id (pre-scoping uploads) — they remain accessible but unscoped.

ALTER TABLE platform.files
    ADD COLUMN IF NOT EXISTS app_id UUID NULL;

-- Blob path was previously: {businessCode}/{category}/{fileId}.ext
-- New blob path:             {businessCode}/{appId}/{category}/{fileId}.ext
-- Existing blobs are unaffected — blob_name is stored as-is and used verbatim.

CREATE INDEX IF NOT EXISTS idx_files_app_id
    ON platform.files(app_id) WHERE is_deleted = FALSE;
