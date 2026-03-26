ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS app_id UUID,
    ADD COLUMN IF NOT EXISTS app_slug TEXT,
    ADD COLUMN IF NOT EXISTS tech_stack TEXT;

CREATE UNIQUE INDEX IF NOT EXISTS uq_projects_app_slug
    ON platform.projects(app_slug) WHERE app_slug IS NOT NULL;
