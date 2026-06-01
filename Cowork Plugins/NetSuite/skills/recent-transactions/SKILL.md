---
name: recent-transactions
description: |
  List recent NetSuite transactions for a customer, vendor, or across the
  business. Use when the user asks "show recent transactions", "what
  happened in NetSuite this week", "recent activity for [entity]", "recent
  invoices/orders/bills", or "transaction history for [entity]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Finance
cowork.icon: History
---

# Recent Transactions

1. If the user names an entity, resolve them via `search_customers` or `search_vendors` and capture the entity id.
2. Run `run_suiteql` against the transaction table for the last 30 (or requested) days, ordered by `trandate DESC`. Filter by `entity` when an id is known and/or `type IN ('CustInvc','SalesOrd','PurchOrd','VendBill','CustPymt','VendPymt')` when the user requests a slice.
3. Group results by type and surface counts and totals.
4. Highlight the largest 3-5 transactions by `foreigntotal`.
5. End with one suggested follow-up (e.g., "review draft invoices", "approve pending bills").
