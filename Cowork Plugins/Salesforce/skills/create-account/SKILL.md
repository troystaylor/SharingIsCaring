---
name: create-account
description: |
  Creates a new Salesforce account. Use when the user asks "create a new
  Salesforce account", "add [company] to Salesforce", "spin up a new
  account in Salesforce", "register [company] in Salesforce", or "create
  a Salesforce account for [name]".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: Building
---

# Create Account

1. Check the account does not already exist using `search_accounts` (warn the user if a near-duplicate is found).
2. Gather the required field (name) and recommended fields (type, industry, website, billing city/country, annual revenue).
3. Summarize the proposed account and ask for explicit user confirmation before writing.
4. Create the account via `create_account` and capture the new Account Id.
5. Offer the standard onboarding chain:
   - add the primary buyer contact via `create_contact`
   - create an initial opportunity via `create_opportunity`
   - log a first-touch task via `create_task`
6. Return the new Account Id and Ids for any contact, opportunity, or task created.
