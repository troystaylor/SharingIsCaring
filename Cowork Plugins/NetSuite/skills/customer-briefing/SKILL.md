---
name: customer-briefing
description: |
  Builds a NetSuite customer briefing using SuiteQL and record reads. Use when
  the user asks "brief me on [customer] in NetSuite", "prepare me for this
  customer", "summarize NetSuite customer status", "look up [customer] in
  NetSuite", or "what is happening with this account in NetSuite".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Finance
cowork.icon: Briefcase
---

# Customer Briefing

1. Resolve the customer using `search_customers` and capture the customer id.
2. Pull full customer details with `get_record` (recordType `customer`, fields like `companyname,email,phone,terms,creditlimit,balance,daysoverdue,lastmodifieddate`).
3. Pull open sales orders with `get_open_sales_orders` for that customer id.
4. Pull open invoices with `get_open_invoices` for that customer id.
5. Pull recent transactions via `run_suiteql` against the transaction table (last 90 days) for that entity.
6. Summarize customer status, terms and credit, open pipeline, AR exposure, and recent activity.
7. End with three concrete prep actions for the next customer touchpoint.
