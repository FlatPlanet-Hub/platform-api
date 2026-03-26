-- Migration 006: Add app_slug to platform.projects
-- Run manually against Supabase before deploying Step 5.
-- app_id already exists from migration 004.

ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS app_slug TEXT;

CREATE INDEX IF NOT EXISTS idx_projects_app_slug ON platform.projects (app_slug);
