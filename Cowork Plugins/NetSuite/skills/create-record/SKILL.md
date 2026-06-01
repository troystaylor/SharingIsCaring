---
name: create-record
description: |
  Create a new NetSuite record of any type. Use when the user asks "create
  a [recordType] in NetSuite", "add a new customer/vendor/SO/invoice",
  "open a new NetSuite record", or "I need a new [record] with these
  fields".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: action
cowork.category: Finance
cowork.icon: PlusCircle
---

# Create Record

1. Confirm `recordType` and the fields the user wants to populate.
2. If unsure about required fields, call `get_record_metadata` for the type first.
3. Echo the proposed `fields` payload back to the user and ask for explicit confirmation before writing.
4. On confirmation, call `create_record` with `recordType` and `fields`.
5. Report the new record id and any server-returned warnings. Suggest verifying with `get_record`.
