---
name: account-briefing
description: |
  Builds a Salesforce account briefing for customer prep using Salesforce CRM
  data. Use when the user asks "brief me on [account] in Salesforce", "prepare
  me for the [account] meeting", "summarize Salesforce account health", "look
  up [account] in Salesforce", "account 360 for [account]", or "what changed
  on this Salesforce account recently".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: Building
---

# Account Briefing

## What This Skill Does

Assembles a single-page account snapshot from Salesforce — company profile,
open pipeline, key stakeholders, recent activity, and outstanding follow-ups —
so the user can walk into a customer conversation prepared.

The Salesforce connector exposes generic sObject tools, not per-entity tools,
so this skill supplies the exact SOQL to run.

## When to Activate

- User asks to be briefed or prepped on a customer, account, or company
- User asks what is happening with an account
- User asks who the contacts are at an account and what is open with them

## Workflow

1. **Resolve the account.** Run `find` (SOSL) to locate candidates:

   ```
   FIND {Acme*} IN NAME FIELDS RETURNING Account(Id, Name, Type, Industry, Website, Owner.Name LIMIT 10)
   ```

   If more than one plausible match, list them and ask which one. Never guess
   between similarly named accounts.

2. **Pull the account record** with `soqlQuery`:

   ```sql
   SELECT Id, Name, Type, Industry, AnnualRevenue, NumberOfEmployees, Website,
          Phone, BillingCity, BillingState, BillingCountry, Description,
          Owner.Name, LastModifiedDate, LastActivityDate
   FROM Account WHERE Id = '001XXXXXXXXXXXXXXX'
   ```

3. **Pull open pipeline:**

   ```sql
   SELECT Id, Name, StageName, Amount, CloseDate, Probability, NextStep,
          ForecastCategoryName, Owner.Name, LastModifiedDate
   FROM Opportunity
   WHERE AccountId = '001XXXXXXXXXXXXXXX' AND IsClosed = false
   ORDER BY CloseDate ASC LIMIT 25
   ```

4. **Pull recent closed business** for relationship context:

   ```sql
   SELECT Id, Name, StageName, Amount, CloseDate, IsWon
   FROM Opportunity
   WHERE AccountId = '001XXXXXXXXXXXXXXX' AND IsClosed = true
     AND CloseDate = LAST_N_DAYS:365
   ORDER BY CloseDate DESC LIMIT 10
   ```

5. **Pull key contacts:**

   ```sql
   SELECT Id, Name, Title, Email, Phone, MobilePhone, LastActivityDate
   FROM Contact WHERE AccountId = '001XXXXXXXXXXXXXXX'
   ORDER BY LastActivityDate DESC NULLS LAST LIMIT 20
   ```

6. **Pull open follow-ups and recent completed activity:**

   ```sql
   SELECT Id, Subject, ActivityDate, Status, Priority, Owner.Name, WhoId, WhatId
   FROM Task
   WHERE AccountId = '001XXXXXXXXXXXXXXX' AND IsClosed = false
   ORDER BY ActivityDate ASC NULLS LAST LIMIT 20
   ```

   ```sql
   SELECT Id, Subject, ActivityDate, Description, Owner.Name
   FROM Task
   WHERE AccountId = '001XXXXXXXXXXXXXXX' AND IsClosed = true
     AND ActivityDate = LAST_N_DAYS:60
   ORDER BY ActivityDate DESC LIMIT 15
   ```

7. **Pull open cases** if the account is an existing customer:

   ```sql
   SELECT Id, CaseNumber, Subject, Status, Priority, CreatedDate
   FROM Case WHERE AccountId = '001XXXXXXXXXXXXXXX' AND IsClosed = false
   ORDER BY CreatedDate ASC LIMIT 10
   ```

8. **Synthesize the briefing** using the output format below. Finish with three
   concrete prep actions drawn from the data, not generic advice.

## Output Format

**Acme Corporation** — Technology · 4,200 employees · Owner: Jamie Chen

**Snapshot**
- Open pipeline: **$1.2M** across 4 opportunities
- Nearest close: Acme Platform Renewal, $450K, 2026-09-30 (Negotiation)
- Won in last 12 months: $780K across 2 deals
- Open cases: 2 (1 High)

**Open opportunities**

| Opportunity | Stage | Amount | Close | Next Step |
|---|---|---|---|---|
| Acme Platform Renewal | Negotiation | $450,000 | 2026-09-30 | Legal redlines |

**Key stakeholders**

| Name | Title | Last Activity |
|---|---|---|
| Dana Reyes | VP Engineering | 2026-08-04 |

**Recent activity (60 days)** — short bullets of completed tasks and calls.

**Open follow-ups** — bullets with owner and due date; flag anything overdue.

**Three things to do before the meeting** — specific and sourced from the data.

## Handling Edge Cases

- **No account match:** Say so plainly and offer to search Leads instead with
  `find` returning `Lead(Id, Name, Company, Status, Email)`.
- **Multiple matches:** List Id, city, and owner, then ask which one.
- **Empty pipeline:** State it directly — an account with no open opportunities
  is itself a finding worth calling out.
- **Custom fields:** If the user asks about a field not listed above, call
  `getObjectSchema` with `object-name: Account` to discover the real API name
  before querying. Never invent field API names.
- **Person Accounts:** If the Account schema includes `IsPersonAccount` and it
  is true, contact data lives on the Account itself — check the schema before
  assuming a separate Contact record exists.
- **Apostrophes in names:** Escape single quotes in SOQL string literals as
  `\'` (for example `Name LIKE '%O\'Brien%'`).

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to pull that briefing. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills — the Salesforce session may have expired.
4. If a query fails with an insufficient-access or field-not-accessible error,
   explain that the connector runs as their own Salesforce user, so results are
   limited by their profile, sharing rules, and field-level security.

## Additional Resources

- **`references/crm-object-reference.md`** — Standard field API names, picklist
  values, and relationship paths for Account, Opportunity, Contact, Lead, Case,
  Task, and Event.
