-- Migration 007: drop stale FK constraints on api_tokens
-- platform.users and platform.apps are owned by the Security Platform (separate DB).
-- HubApi stores only user_id / app_id as plain UUIDs — no referential integrity across services.

ALTER TABLE platform.api_tokens DROP CONSTRAINT IF EXISTS api_tokens_user_id_fkey;
ALTER TABLE platform.api_tokens DROP CONSTRAINT IF EXISTS api_tokens_app_id_fkey;
