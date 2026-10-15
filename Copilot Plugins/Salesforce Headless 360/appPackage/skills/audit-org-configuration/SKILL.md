---
name: audit-org-configuration
description: Review Salesforce org configuration read-only through Headless 360 - named credentials, event integrations, users and permission assignments, Apex triggers. Use when asked to audit, inventory or check how something is configured in Salesforce.
---

# Audit Org Configuration

## When to use this skill

- The user wants to understand or document the current setup without changing it.
- Cue phrases: "list our named credentials", "what triggers are on Account", "who has this permission set", "audit our event relays".

## How to do it

1. Call `discover` with the area to audit and select read operations; call `describe` to confirm the endpoint and any required parameters.
2. Retrieve configuration only with `dispatch_readonly` (GET). Do not call `dispatch` in this skill.
3. Page through results and gather: item name, type, status, last modified, owner, and any notable settings.
4. Report findings in a table grouped by area, then list observations such as unused or overly broad items, missing documentation, and risky settings.
5. Offer follow-up actions and point to the Run Setup Task or Manage User Access skills for changes.

## What to watch for

- `dispatch_readonly` only allows GET; if an operation needs another method, it is not a read operation, so stop and ask.
- Do not display secrets or credentials; report that they exist without values.
- Headless 360 is Beta; coverage is limited to what `discover` returns today.
