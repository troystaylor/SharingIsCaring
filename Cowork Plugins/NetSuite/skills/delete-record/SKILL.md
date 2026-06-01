---
name: delete-record
description: |
  Permanently delete a NetSuite record. Use only when the user explicitly
  asks to "delete [recordType] [id]", "remove this NetSuite record",
  "purge [record]". Irreversible.
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: action
cowork.category: Finance
cowork.icon: Trash2
---

# Delete Record

1. Confirm `recordType` and `recordId`.
2. Call `get_record` to display the record being deleted so the user can confirm identity.
3. Warn the user that delete is permanent and ask for explicit confirmation (case-sensitive "delete" or "yes").
4. On confirmation, call `delete_record`.
5. If the server returns a referential-integrity error, surface the linked record and suggest archiving (`isinactive = T`) via `update-record` instead.
