-- Migration 010: Add project_type and auth_enabled to platform.projects

ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS project_type  TEXT    NOT NULL DEFAULT 'fullstack',
    ADD COLUMN IF NOT EXISTS auth_enabled  BOOLEAN NOT NULL DEFAULT false;
