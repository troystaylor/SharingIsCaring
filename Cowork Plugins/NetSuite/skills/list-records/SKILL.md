---
name: list-records
description: |
  List or filter NetSuite records of a given type using the REST collection
  endpoint. Use when the user asks "list [recordType] in NetSuite", "show
  me all [customers/items/etc]", "find records where [field] [op] [value]",
  or "filter NetSuite [recordType]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Finance
cowork.icon: List
---

# List Records

1. Confirm the `recordType` (e.g., `customer`, `salesorder`, `invoice`, `item`).
2. Build a filter using NetSuite collection-filter syntax (e.g., `email START_WITH "alex"`, `lastmodifieddate AFTER "2024-01-01"`).
3. Call `list_records` with `recordType`, optional `filter`, `fields`, `limit`, `offset`, `expandSubResources`.
4. Note: without a `fields` list, results return only id + HATEOAS links. For richer queries prefer `run-suiteql-query`.
5. If many rows, suggest paginating with `offset` or switching to SuiteQL.
