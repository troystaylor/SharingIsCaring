---
name: CAB Submission
description: >
  Generate a Change Advisory Board submission from Teams threads,
  emails, or conversation context. Use when user asks for a CAB
  submission, change request, or change advisory document.
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: change advisory, CAB, change request, governance, ITIL
commands:
  - /cab
  - /change-request
  - /cab-submission
---

## Context

The organization requires a structured CAB submission for all
production changes. This skill defines the required format.
The agent should gather information from Teams threads, emails,
and conversation context to populate each section.

## Inputs

Before generating, gather:
1. Change description (from the user or a Teams thread)
2. Affected systems or services
3. Planned date and time window
4. Requestor name and team

If any input is missing, ask the user for it.

## Steps

1. Search email and Teams for context about the change (use discover_graph + invoke_graph)
2. Extract key details: what's changing, why, who requested it
3. Assess risk based on scope (single service = low, cross-system = medium, production data = high)
4. Generate the CAB submission in the format below
5. Present to the user for review before final delivery

## Output Format

```
CHANGE ADVISORY BOARD SUBMISSION
================================

Change ID:          [auto-generate: CHG-YYYY-MM-DD-NNN]
Submission Date:    [today's date]
Requestor:          [name, team]
Approver:           [to be assigned]

1. CHANGE SUMMARY
   [2-3 sentence description of the change]

2. BUSINESS JUSTIFICATION
   [Why this change is needed]

3. AFFECTED SYSTEMS
   - [System/service 1]
   - [System/service 2]

4. RISK ASSESSMENT
   Risk Level:      [Low / Medium / High / Critical]
   Risk Factors:    [bullet list]
   Mitigation:      [what's being done to reduce risk]

5. IMPLEMENTATION PLAN
   Start Window:    [date, time]
   End Window:      [date, time]
   Steps:
   1. [Step 1]
   2. [Step 2]

6. ROLLBACK PROCEDURE
   Trigger:         [when to rollback]
   Steps:
   1. [Rollback step 1]
   2. [Rollback step 2]
   Estimated Time:  [duration]

7. TESTING
   Pre-change:      [validation steps]
   Post-change:     [verification steps]

8. STAKEHOLDER NOTIFICATIONS
   - [person/team to notify before]
   - [person/team to notify after]

9. APPROVAL
   [ ] Change Manager
   [ ] Technical Lead
   [ ] Business Owner
```

## Constraints

- Never auto-submit a CAB — always present to user for review first
- Risk level must be justified with specific factors
- Rollback procedure is mandatory — never leave it blank
- If the change affects production data, risk must be Medium or higher
- Include at least 2 rollback steps
- Stakeholder list must include at least the requestor's manager
