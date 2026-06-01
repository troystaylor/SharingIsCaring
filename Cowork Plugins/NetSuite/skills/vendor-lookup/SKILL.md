---
name: vendor-lookup
description: |
  Find a NetSuite vendor and review their contact info and recent activity.
  Use when the user asks "look up vendor [name]", "find supplier in
  NetSuite", "vendor contact info", "is [name] a NetSuite vendor", or
  "recent POs with [vendor]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Finance
cowork.icon: Truck
---

# Vendor Lookup

1. Resolve the vendor using `search_vendors` and capture the vendor id.
2. Pull full vendor details with `get_record` (recordType `vendor`).
3. Optionally pull recent POs and bills with `run_suiteql` against the transaction table filtered by `entity = <vendorId>` and `type IN ('PurchOrd', 'VendBill')`.
4. Summarize contact info, terms, and any open POs or unpaid bills.
5. End with one or two next-step suggestions (e.g., "verify W-9 on file", "reconcile open bill").
