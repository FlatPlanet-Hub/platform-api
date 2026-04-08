-- 009: Add Azure App Service fields to platform.projects
ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS azure_app_service_name TEXT,
    ADD COLUMN IF NOT EXISTS azure_app_service_url  TEXT;
