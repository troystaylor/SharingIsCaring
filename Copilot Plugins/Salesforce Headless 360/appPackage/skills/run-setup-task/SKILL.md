---
name: run-setup-task
description: Carry out a Salesforce Setup or platform task through Headless 360 using the discover, describe, dispatch flow, with a plan and confirmation before any change. Use when asked to configure something in Salesforce such as a named credential, an event relay, an Apex trigger or a platform event integration.
---

# Run Setup Task

## When to use this skill

- The user wants an admin or developer configuration task done in Salesforce rather than clicking through Setup.
- Cue phrases: "set up a named credential for...", "create a change data capture integration", "deploy this Apex trigger".

## How to do it

1. Call `discover` with the task in natural language (use `resultType` `sor` for multi-step recipes and `endpoint` for single operations). Review the ranked candidates.
2. Call `describe` on the best candidate to read its APIs, parameters, dependencies and ordered steps. Confirm it matches the request.
3. Present a plan: each step, the exact URL, method and payload, the expected effect, and whether it is read-only or changes the org.
4. Run read-only steps with `dispatch_readonly` to gather current state first.
5. For each changing step, get explicit confirmation, then call `dispatch` with the described `url`, `method`, `headers`, `queryParams` and `body`. Check the result before moving to the next step.
6. Summarize what changed and how to verify or roll it back.

## What to watch for

- Headless 360 is a Beta server; the set of operations grows over time, so always rely on `discover` rather than assumptions.
- Do configuration changes in a sandbox or Developer org before production.
- Never chain changing steps without confirming; stop on the first error and report it.
- Actions run as the signed-in user with their permissions, and are attributed to them in the audit trail.
