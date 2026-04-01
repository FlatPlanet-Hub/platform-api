ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS github_repo_name   TEXT,
    ADD COLUMN IF NOT EXISTS github_branch      TEXT NOT NULL DEFAULT 'main',
    ADD COLUMN IF NOT EXISTS github_repo_link   TEXT;
