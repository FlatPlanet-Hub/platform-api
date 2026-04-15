-- Migration 015: Add storage bucket_name to platform.projects
-- Bucket is created lazily on first upload or explicitly via POST /api/v1/projects/{id}/storage/provision
-- NULL until provisioned.

ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS bucket_name TEXT;

CREATE UNIQUE INDEX IF NOT EXISTS idx_projects_bucket_name
    ON platform.projects(bucket_name)
    WHERE bucket_name IS NOT NULL;
