---
name: compare-backups-and-plan-restore
description: Compare two Salesforce backups, review what changed or was deleted, select records for restore and confirm counts - stopping before the restore itself, which runs outside MCP. Use when asked to find what changed between backups or to prepare a data recovery.
---

# Compare Backups And Plan Restore

## When to use this skill

- The user needs to recover data or understand what changed between two points in time.
- Cue phrases: "compare my last two backups", "what was deleted from Account", "prepare a restore of these records".

## How to do it

1. Call `get_backups` and agree with the user which two backups to compare (`fromBackupId` older, `toBackupId` newer) and which objects, or `["all"]`.
2. After confirmation, call `create_compare_activity` and keep the returned `activityId`. It is asynchronous; poll `get_compare_activity` until complete.
3. Read per-object change counts with `get_compare_activity_summaries` and summarize what changed and what was deleted.
4. If the user wants records restored, build the selection with `update_selection_records` using filter expressions and `selectedForRestore=true`. Show the filter first and confirm; the last matching batch wins.
5. Verify with `get_selection_counts_by_type` per object and `get_restore_activity_record_selection_metadata`, and report the counts.
6. Tell the user the restore itself cannot run through MCP and must be started in the Backup and Recover UI or another non-MCP surface. After they run it, review results with `get_restore_activity`, `get_restore_activity_summary` and `get_restore_activity_object_results`.

## What to watch for

- `update_selection_records` works on COMPARE activities only and overwrites the prior selection.
- Tool calls have a 30-second budget; poll rather than waiting in one call.
- Never claim a restore happened; this server only guides up to the selection step.
