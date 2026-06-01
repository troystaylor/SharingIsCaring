---
name: run-suiteql-query
description: |
  Execute an arbitrary SuiteQL query against NetSuite. Use when the user
  asks "run a SuiteQL query", "query NetSuite", "select from [table] in
  NetSuite", "ad-hoc NetSuite report", or any time a structured SQL-like
  query is needed.
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Finance
cowork.icon: Database
---

# Run SuiteQL Query

1. Confirm or compose the SuiteQL with the user. Common tables: `customer`, `vendor`, `employee`, `item`, `transaction`, `salesorder`, `purchaseorder`, `invoice`, `account`, `subsidiary`, `location`, `department`.
2. Call `run_suiteql` with the query, plus optional `limit` and `offset`.
3. If `hasMore` is true, offer to page through results using `offset`.
4. Summarize the result shape (columns + row count) before showing tabular data.
5. End with a one-line interpretation or suggested follow-up query.
