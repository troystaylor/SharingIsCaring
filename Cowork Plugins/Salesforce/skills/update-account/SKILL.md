---
name: update-account
description: |
  Updates key Salesforce account fields including type, industry, annual
  revenue, employee count, website, phone, billing address, and owner.
  Use when the user asks "update this Salesforce account", "change
  industry in Salesforce", "update revenue for [account] in Salesforce",
  "reassign Salesforce account owner", or "change [account] details in
  Salesforce".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: Edit
---

# Update Account

1. Confirm the target account using `search_accounts` if needed and capture the Account Id.
2. Fetch current values with `get_account` and summarize proposed changes.
3. Ask for explicit user confirmation before writing.
4. Apply the changes via `update_account`.
5. Return a before/after summary and suggest follow-up tasks such as briefing the new owner.
