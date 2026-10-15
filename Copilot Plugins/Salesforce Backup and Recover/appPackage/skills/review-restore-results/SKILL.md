---
name: review-restore-results
description: Review the outcome of a Salesforce restore or compare - activity status, per-object inserted, updated and failed counts, per-record results and hierarchy results. Use when asked how a restore went, what failed to restore, or what happened to a specific record.
---

# Review Restore Results

## When to use this skill

- A restore has run (in the Backup and Recover UI or another non-MCP surface) and the user wants to know the result.
- Cue phrases: "how did the restore go", "what failed to restore", "show restore results for Account", "was this record restored".

## How to do it

1. Find the activity: call `get_activities` (most recent first) and match type and time to what the user ran. If the activity type is unknown, `get_activity` looks up any activity by Id.
2. Read the rollup with `get_restore_activity` (flat restore) or `get_restore_with_hierarchy` (hierarchy restore, also used to poll progress). Report status, start and end times and overall counts.
3. Drill into objects with `get_restore_activity_summary` (updated, inserted, failed per object, filter with `searchTerm`) and `get_restore_result_counts` for per-status counts on one object.
4. For records, use `get_restore_activity_object_results` (new and old Ids, status, insert or update) or, for hierarchy restores, `get_hierarchy_summary` with `nodePaths` and `get_restore_with_hierarchy_results` (`type` `inserted` for the tree view, `updated` for the flat view).
5. Summarize: what was restored, what failed and the reported reasons, which records got new Ids, and what the user should verify or retry in the UI.

## What to watch for

- This skill is read-only; restore execution, retry and download are not available through MCP.
- Page through large results instead of assuming the first page is complete.
- Inserted records get new Ids; relate them to the old Ids using the result rows before telling the user a record is missing.
