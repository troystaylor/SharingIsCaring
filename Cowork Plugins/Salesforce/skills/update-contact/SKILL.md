---
name: update-contact
description: |
  Updates key Salesforce contact fields including name, title, email,
  phone, department, related account, and owner. Use when the user asks
  "update this Salesforce contact", "change [contact] title in Salesforce",
  "update email for [contact] in Salesforce", "reassign Salesforce contact
  owner", or "move [contact] to a different Salesforce account".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: Edit
---

# Update Contact

1. Confirm the target contact using `search_contacts` if needed and capture the Contact Id.
2. Fetch current values with `get_contact` and summarize proposed changes.
3. Ask for explicit user confirmation before writing.
4. Apply the changes via `update_contact`.
5. Return a before/after summary.
