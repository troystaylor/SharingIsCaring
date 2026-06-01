---
name: ar-aging-snapshot
description: |
  Pulls open NetSuite invoices and surfaces AR aging. Use when the user asks
  "what is my AR aging", "show overdue invoices", "AR aging in NetSuite",
  "who owes us money", "open invoices over 30/60/90 days", or "collections
  list".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Finance
cowork.icon: ClipboardList
---

# AR Aging Snapshot

1. Call `get_open_invoices` with optional `customerId` filter and `minDaysOverdue` for the requested bucket.
2. Bucket invoices into Current, 1-30, 31-60, 61-90, 90+ based on `daysoverdue`.
3. Group totals by customer (top 10 by `foreignamountunpaid`).
4. Highlight invoices > 60 days past due and any single invoice over $25k.
5. End with up to three recommended collection actions (e.g., email reminder, phone follow-up, hold further shipments).
