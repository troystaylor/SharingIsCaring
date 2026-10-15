---
name: run-on-demand-backup
description: Start an on-demand Salesforce backup of the configured policy and track it to completion. Use when asked to take a backup now, for example before a risky data change or deployment.
---

# Run On Demand Backup

## When to use this skill

- The user wants a fresh backup right now.
- Cue phrases: "back up Salesforce now", "take a backup before the data load", "start an on-demand backup".

## How to do it

1. Call `get_latest_policy` and tell the user what will be backed up, because the object scope is policy-driven and cannot be chosen per call.
2. Call `get_backups` to see recent and in-progress backups. On-demand backups are limited to three per day, including those started in the UI or API.
3. After explicit confirmation, call `enqueue_backup`.
4. If the response has `queued` true, keep the `backupId` and poll `get_backup_by_id` until it finishes, reporting status. If not queued, explain the `enqueueRejectionReason` (for example `DAILY_ON_DEMAND_LIMIT_REACHED`, `SAME_TYPE_AND_CONTENT_ALREADY_QUEUED` or `DELTA_REQUIRES_FULL_COMPLETED`) and suggest next steps.
5. When complete, summarize result, duration and per-object detail from `get_backup_objects` if asked.

## What to watch for

- Do not call `enqueue_backup` repeatedly; it consumes a limited daily allowance.
- Polling is needed because the tool returns immediately; do not block waiting.
- Aborting backups and changing policy are intentionally not available through this server.
