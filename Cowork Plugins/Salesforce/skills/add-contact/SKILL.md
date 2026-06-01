---
name: add-contact
description: |
  Creates a new Salesforce contact and optionally links it to an account.
  Use when the user asks "add a contact in Salesforce", "create a new
  Salesforce contact", "add [person] as a contact at [account]", "log a
  new buyer in Salesforce", or "create a Salesforce contact for [name]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: PersonAdd
---

# Add Contact

1. If an account is mentioned, resolve it with `search_accounts` and capture the Account Id.
2. Check for an existing contact with the same name or email via `search_contacts` to avoid duplicates.
3. Collect required field (last name) and optional fields (first name, title, email, phone, department).
4. Summarize the proposed contact and ask for explicit user confirmation before writing.
5. Create the contact via `create_contact` and capture the new Contact Id.
6. Offer to schedule an intro touchpoint by creating a follow-up task via `create_task` (link to the contact's account as `whatId`).
7. Return the new Contact Id and any follow-up task created.
