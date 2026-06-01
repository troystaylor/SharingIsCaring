---
name: get-record-details
description: |
  Retrieve a single NetSuite record by id. Use when the user asks "get
  record [id]", "show me [recordType] [id]", "open NetSuite [recordType]
  [id]", "fetch full details for [record]", or "what is on record [id]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Finance
cowork.icon: FileText
---

# Get Record Details

1. Confirm `recordType` and `recordId`.
2. Optionally narrow fields with a `fields` CSV when the user only needs a subset.
3. Call `get_record`. Set `expandSubResources=true` only when sublist/line data is needed (it makes the response significantly larger).
4. Summarize the record's key fields. Offer to load sublists via `get_sublist` if relevant.
5. Never echo back internal sensitive fields (e.g., `socialsecuritynumber`) without asking.
