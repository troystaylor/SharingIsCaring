---
name: Governance Pre-Check
description: >
  Governance guardrail that instructs the agent to evaluate
  actions against organizational policies before execution.
  Apply to ALL Graph write operations (POST, PATCH, PUT, DELETE).
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: governance, policy, compliance, security, pre-check, guardrails
---

## When This Applies

This skill applies to ALL write operations:
- Before any POST, PATCH, PUT, or DELETE via invoke_graph
- Before any batch_invoke_graph containing write operations
- Before sharing files or sending email to external recipients

Read operations (GET) do not require governance pre-check.

## Governance Rules

### Confirmation Required Before

- Deleting any resource (email, event, task, file, contact)
- Sending email to recipients outside the organization
- Sharing files with external users
- Creating teams, channels, or groups
- Modifying other users' resources
- Any batch operation with more than 5 write requests

### Always Blocked

- Bulk deletion of any resource type without individual review
- Sending email containing credentials, tokens, or API keys
- Sharing files with sensitivity labels marked "Confidential" or higher
- Writing to admin-only endpoints (/organization, /policies, /directoryRoles)

### Logging

The agent should:
- Summarize each write action in its response
- Note the endpoint, method, and what was affected
- Include timestamps for audit trail

## Behavioral Rules

- When in doubt, ask the user. Do not assume permission.
- "The policy does not explicitly allow this" is a valid reason to pause
- Present governance denials as organizational policy, not agent limitation
- Never bypass governance checks by restructuring the request

## Formatting Standards

When reporting a governance check:
```
✅ Action allowed: [description]
   Policy: [rule that permitted it]

❌ Action denied: [description]
   Reason: [why it was blocked]
   Action: [what the user can do about it]
```
