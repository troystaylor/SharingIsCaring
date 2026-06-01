---
name: create-opportunity
description: |
  Creates a new Salesforce opportunity linked to an account. Use when the
  user asks "create a new Salesforce opportunity", "open a new deal in
  Salesforce", "spin up a Salesforce opportunity for [account]", "log a
  new pursuit in Salesforce", or "start a Salesforce opportunity at
  [stage] worth [amount]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: AddPin
---

# Create Opportunity

1. Resolve the target account with `search_accounts` and confirm the Account Id.
2. Check for an existing duplicate with `search_opportunities` filtered to the account name.
3. Gather required fields: name, stage, close date. Confirm optional fields like amount, probability, type, and owner.
4. Summarize the proposed opportunity and ask for explicit user confirmation before writing.
5. Create the opportunity via `create_opportunity` and capture the new Opportunity Id.
6. Offer to add the primary buyer as a contact via `create_contact` (or look one up with `search_contacts`).
7. Offer to create the first next-step task via `create_task` using the new Opportunity Id as `whatId`.
8. Return the new Opportunity Id, a one-line summary, and links to any contact/task created.
