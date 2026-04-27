-- Migration 017: add netlify_site_url to platform.projects
-- Stores the full Netlify site URL (e.g. https://fp-mayari.netlify.app)
-- Used to set ALLOWED_ORIGINS / Cors__AllowedOrigins__0 on Azure App Service provisioning.

ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS netlify_site_url TEXT;
