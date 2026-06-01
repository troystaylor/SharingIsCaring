---
name: log-call-notes
description: |
  Logs Salesforce call outcomes and follow-up work as tasks in Salesforce.
  Use when the user asks "log my Salesforce call notes", "capture Salesforce
  meeting notes", "record next steps in Salesforce", "add a follow-up task
  for this Salesforce opportunity", or "create a Salesforce task from this
  call".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: NotePin
---

# Log Call Notes

1. Confirm the opportunity and account context (use `search_opportunities` or `search_accounts` if needed).
2. Structure notes into outcomes, objections, commitments, and next steps.
3. Ask for confirmation before writing anything.
4. Use `create_task` for each follow-up action, linking it to the opportunity via `whatId`.
5. If the call changed deal posture (stage, next step, close date, probability), apply those edits via `update_opportunity`.
6. Summarize what was logged, what was updated on the opportunity, and who owns each next step.
