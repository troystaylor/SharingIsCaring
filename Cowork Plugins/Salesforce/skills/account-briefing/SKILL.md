---
name: account-briefing
description: |
  Builds a Salesforce account briefing for customer prep using Salesforce CRM
  data. Use when the user asks "brief me on [account] in Salesforce", "prepare
  me for this customer", "summarize Salesforce account health", "look up
  [account] in Salesforce", "retrieve [account] from Salesforce", or "what
  changed on this Salesforce account this week".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: Building
---

# Account Briefing

1. Resolve the account using `search_accounts` and capture the Account Id.
2. Pull full account details with `get_account`.
3. Pull top open opportunities with `search_opportunities` for that account.
4. Pull key contacts at the account with `search_contacts` filtered by Account Id.
5. Pull open follow-ups with `list_tasks` using `whatId` (account) and `openOnly=true`.
6. Summarize account status, recent changes, open pipeline, key stakeholders, and outstanding follow-ups.
7. End with three concrete prep actions for the next customer interaction.
