---
name: open-sales-orders-review
description: |
  Review open NetSuite sales orders by customer, status, or value threshold.
  Use when the user asks "what sales orders are open", "show me open SOs in
  NetSuite", "open orders for [customer]", "sales orders over $X", or
  "pending fulfillment in NetSuite".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: ShoppingCart
---

# Open Sales Orders Review

1. If the user names a customer, resolve them with `search_customers` and capture the customer id.
2. Call `get_open_sales_orders` with optional `customerId` and `minTotal` filters.
3. For any order needing detail (e.g., line items), call `get_sublist` (sublistId `item`) on the sales order record.
4. Summarize totals, oldest open orders, and customers with the largest open backlog.
5. End with one or two suggested follow-up actions (e.g., "expedite SO-1234", "follow up with customer X").
