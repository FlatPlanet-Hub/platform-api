-- Migration 016: add netlify_site_id to projects
-- Enables auto-push of VITE_PLATFORM_TOKEN and VITE_API_URL to Netlify
-- on token regeneration and Azure provisioning respectively.

ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS netlify_site_id TEXT;
