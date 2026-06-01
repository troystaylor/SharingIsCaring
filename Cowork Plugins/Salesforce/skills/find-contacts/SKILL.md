---
name: find-contacts
description: |
  Finds Salesforce contacts by name, email, or related account and returns
  their details. Use when the user asks "find contacts in Salesforce",
  "who are my contacts at [account]", "look up [person] in Salesforce",
  "search Salesforce contacts", "get contact details from Salesforce",
  or "show me contacts for [account]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: People
---

# Find Contacts

1. If an account name is mentioned, resolve it with `search_accounts` and capture the Account Id.
2. Use `search_contacts` with the account filter and/or search term to list candidates.
3. If the user asks for full detail on one contact, pull it with `get_contact`.
4. To gauge engagement, pull recent activities with `list_recent_activities` filtered to the related Account Id.
5. Summarize each contact's role, contact info, account affiliation, and recent engagement.
6. Suggest next-step actions such as logging a call (`create_task`), updating contact data (`update_contact`), or briefing the account (`get_account`).
