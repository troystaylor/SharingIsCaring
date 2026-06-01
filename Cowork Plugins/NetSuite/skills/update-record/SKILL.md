---
name: update-record
description: |
  Patch fields on an existing NetSuite record. Use when the user asks
  "update [recordType] [id]", "change the [field] on [record]", "edit
  NetSuite record", "fix the [field] on [record]", or "set [field] to
  [value]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: action
cowork.category: Finance
cowork.icon: Edit3
---

# Update Record

1. Confirm `recordType`, `recordId`, and the exact fields to change.
2. Optionally call `get_record` first to show current values for the fields being changed.
3. Echo the proposed `fields` payload back to the user (old -> new) and ask for explicit confirmation.
4. On confirmation, call `update_record`. Only included fields are changed (PATCH semantics).
5. Confirm success and offer to re-read the record to verify.
