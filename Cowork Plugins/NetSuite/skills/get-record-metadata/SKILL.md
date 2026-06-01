---
name: get-record-metadata
description: |
  Discover NetSuite record types and field schemas. Use when the user asks
  "what fields does [recordType] have", "show me the schema for
  [recordType]", "what record types are available in NetSuite", "list
  NetSuite record types", or "metadata for [recordType]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Finance
cowork.icon: BookOpen
---

# Get Record Metadata

1. If the user wants the full catalog of record types, call `list_record_types`.
2. For a specific type, call `get_record_metadata` with that `recordType`.
3. Summarize: list required fields first, then optional fields, then sublists.
4. Use this output to guide subsequent `create_record` or `update_record` payloads.
5. Note: custom sublists are not exposed through REST Web Services.
