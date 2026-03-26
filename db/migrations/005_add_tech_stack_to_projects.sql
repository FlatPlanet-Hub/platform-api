-- Migration 005: Add tech_stack to platform.projects
-- Used by ClaudeConfigService to include tech stack context in CLAUDE.md

ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS tech_stack text;
