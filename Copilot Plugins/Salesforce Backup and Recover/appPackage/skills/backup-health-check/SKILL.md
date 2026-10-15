---
name: backup-health-check
description: Check the health of Salesforce Backup and Recover - recent backups and status, current backup policy, retention rules and recent audit events. Use when asked whether backups are running, what the policy is, or to review backup coverage.
---

# Backup Health Check

## When to use this skill

- The user asks about backup status, policy, retention or audit history.
- Cue phrases: "are our backups healthy", "show recent backups", "what is our retention", "who changed the backup policy".

## How to do it

1. Call `get_backups` with `sort` `startTimestamp` and `sortDir` `DESC` to list recent backups. Note status, type and timing.
2. For any backup that failed, is partial or is still running, call `get_backup_by_id` and, if useful, `get_backup_objects` for per-object detail.
3. Call `get_latest_policy` for schedules, exclusions and file or attachment inclusion, and `get_retention_rules` for cadence to retention years.
4. Call `get_audit_events` for recent policy or backup changes.
5. Report: last successful backup and its age, any failures with causes, policy summary, retention summary, and recommendations (for example a gap in coverage or an excluded object that should be included).

## What to watch for

- All tools in this skill are read-only.
- Present timestamps with their timezone and compute ages explicitly.
- Only report what the tools returned; flag unknowns instead of guessing.
