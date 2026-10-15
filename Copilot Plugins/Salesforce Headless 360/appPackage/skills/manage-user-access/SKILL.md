---
name: manage-user-access
description: Manage Salesforce users and access through Headless 360 - look up users, assign permission sets, freeze or deactivate accounts - with explicit confirmation for every change. Use when asked to onboard, offboard or adjust access for a Salesforce user.
---

# Manage User Access

## When to use this skill

- The user needs to create, deactivate, freeze or change permissions for a Salesforce user.
- Cue phrases: "give the new rep the Sales permission set", "deactivate Jordan's account", "freeze this user".

## How to do it

1. Call `discover` with the intent (for example "assign permission set to user" or "deactivate user"), then `describe` the chosen operation to learn the exact endpoint and payload.
2. Identify the user and current state with `dispatch_readonly` (GET): username, active status, profile, existing permission sets and licenses.
3. Present the proposed change as a before and after, including who is affected and the access impact.
4. After explicit confirmation, apply it with `dispatch`. Then re-read state with `dispatch_readonly` and report the verified result.

## What to watch for

- Offboarding and freezing are high impact; confirm the exact user and say what stops working.
- Never grant broader access than requested, and do not guess permission set names - list them first.
- Prefer a sandbox for trials. Every action is attributed to the signed-in admin.
- Headless 360 is Beta; if `discover` returns no suitable operation, say so instead of improvising an API call.
