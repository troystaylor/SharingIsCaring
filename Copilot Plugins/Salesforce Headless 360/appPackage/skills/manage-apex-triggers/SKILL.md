---
name: manage-apex-triggers
description: Read, review and deploy Apex triggers through Headless 360 - list triggers on an object, show their source, and deploy a new or updated trigger after review and confirmation. Use when asked what triggers exist, to review trigger code, or to deploy or change a trigger.
---

# Manage Apex Triggers

## When to use this skill

- The user wants to inspect or change Apex trigger code in the org.
- Cue phrases: "what triggers are on Account", "show me that trigger", "deploy this trigger", "disable this trigger".

## How to do it

1. Call `discover` for the intent (for example "list Apex triggers", "get Apex trigger source", "deploy Apex trigger") and `describe` each chosen operation to get its endpoint, parameters and steps.
2. Inspect with `dispatch_readonly` (GET): triggers on the object, their status (active or inactive), API version and source. Summarize what each does and the events it handles (before and after insert, update, delete).
3. For a new or changed trigger, show the full proposed code and a plain-language summary of its behavior, plus risks: recursion, bulk safety (handle lists, no SOQL or DML in loops), and interaction with existing triggers, flows and validation rules.
4. State that deploying Apex to production normally requires passing tests and sufficient coverage, and that a sandbox deployment should come first.
5. After explicit confirmation, deploy with `dispatch` using the described endpoint, then check the result with `dispatch_readonly` and report any compile or test errors verbatim.

## What to watch for

- Trigger code runs on every matching record change; never deploy code the user has not reviewed.
- Do not paste or request secrets in code.
- Do not modify managed-package triggers.
- Headless 360 is Beta; if `discover` returns no suitable operation, say so instead of improvising an API call.
