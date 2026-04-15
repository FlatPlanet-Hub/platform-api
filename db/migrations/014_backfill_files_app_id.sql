-- Migration 014: Backfill app_id for files uploaded before scoping was introduced (2026-04-15)
--
-- Files uploaded before migration 013 have app_id = NULL.
-- These are invisible to project API tokens (which filter by app_id).
--
-- Mapping determined by matching file upload timestamp against the most recently
-- issued API token for each uploader (best proxy for which project was active).
--
-- Uploader: 43beed43-f7f6-4031-be86-df9f904b12ee
--   1 file → FP-ESignature (5feb6718-8cdc-4f6e-b0e8-fdb9e3683ccf)
--   All tokens for this user belong to the same app — safe to target by uploader.
UPDATE platform.files
SET app_id = '5feb6718-8cdc-4f6e-b0e8-fdb9e3683ccf'::uuid
WHERE uploaded_by = '43beed43-f7f6-4031-be86-df9f904b12ee'::uuid
  AND app_id IS NULL
  AND is_deleted = FALSE;

-- Uploader: dc88786a-0b38-43bb-8dc3-7ec36f050ec9
--   4 files → FlatPlanet Development Hub Frontend (a370ab3b-2ca2-49ee-9ba7-563fdb3bc39f)
UPDATE platform.files
SET app_id = 'a370ab3b-2ca2-49ee-9ba7-563fdb3bc39f'::uuid
WHERE id IN (
    '8b7c3033-bacf-42ed-9bac-2fb3c594f948',  -- fp/media/campaign-design-brief.md
    'b4bad2ad-9645-4068-a4af-26970d415c3b',  -- fp/general/test.txt
    '0223fd2a-3f8b-4a1c-a129-353de477d7a5',  -- fp_au/media/FPLogoRBlue 2.png
    '92198954-e76a-44d5-983d-9dfe84473cf1'   -- fp_au/media/FPLogoRBlue 2.png (duplicate upload)
)
AND app_id IS NULL;

--   1 file → Sample3 (949f4572-4034-4f03-9eec-ff245d47b016)
UPDATE platform.files
SET app_id = '949f4572-4034-4f03-9eec-ff245d47b016'::uuid
WHERE id = 'b5ad49e1-d830-4027-9e9b-61f39675107e'  -- fp_au/media/FPLogoRBlue 2.png (Apr 14)
  AND app_id IS NULL;

-- Verify after running:
-- SELECT app_id, COUNT(*) FROM platform.files WHERE is_deleted = FALSE GROUP BY app_id ORDER BY count DESC;
