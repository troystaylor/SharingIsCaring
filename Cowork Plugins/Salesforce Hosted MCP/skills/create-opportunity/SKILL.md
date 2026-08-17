---
name: create-opportunity
description: |
  Creates a new opportunity on a Salesforce account. Use when the user asks
  "create an opportunity in Salesforce", "add a new deal", "log a new pipeline
  opportunity", "open a deal for [account]", or "start tracking this Salesforce
  deal".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: MoneyAdd
---

# Create Opportunity

## What This Skill Does

Creates an Opportunity tied to an account, with the required stage and close
date, after confirming the details with the user.

## When to Activate

- User asks to create a deal, opportunity, or pipeline item in Salesforce
- User describes a new sales pursuit that should be tracked

## Workflow

1. **Resolve the account** — an opportunity should never be created orphaned:

   ```
   FIND {Acme*} IN NAME FIELDS RETURNING Account(Id, Name, Type, Owner.Name LIMIT 10)
   ```

   If the account does not exist, offer to create it first via the
   `create-account` skill.

2. **Check for an existing open deal** so you do not create a duplicate:

   ```sql
   SELECT Id, Name, StageName, Amount, CloseDate
   FROM Opportunity WHERE AccountId = '001XXXXXXXXXXXXXXX' AND IsClosed = false
   ORDER BY CloseDate ASC LIMIT 10
   ```

3. **Gather the required fields.** Salesforce requires `Name`, `StageName`, and
   `CloseDate`:

   | Field | Notes |
   |---|---|
   | `Name` | Required — use the org's convention, e.g. "Acme — Platform Renewal FY27" |
   | `StageName` | Required — must match the org's picklist exactly |
   | `CloseDate` | Required — `YYYY-MM-DD` |
   | `AccountId` | Strongly recommended |
   | `Amount` | Unquoted number |
   | `Type` | e.g. New Business, Existing Business, Renewal |
   | `LeadSource` | Picklist |
   | `NextStep` | Free text — always worth setting |
   | `Description` | |
   | `OwnerId` | Defaults to the signed-in user |

4. **Verify picklists.** Call `getObjectSchema` with
   `object-name: Opportunity` and confirm the exact `StageName`, `Type`, and
   `LeadSource` values. Orgs almost always customize stages — never assume the
   Salesforce defaults.

5. **Confirm before writing.** Show the full record and ask for an explicit yes.

6. **Create it** with `createSobjectRecord`:

   ```json
   {
     "sobject-name": "Opportunity",
     "body": {
       "Name": "Acme — Platform Expansion FY27",
       "AccountId": "001XXXXXXXXXXXXXXX",
       "StageName": "Qualification",
       "CloseDate": "2026-12-31",
       "Amount": 250000,
       "Type": "Existing Business",
       "NextStep": "Discovery workshop scheduled for 2026-09-05"
     }
   }
   ```

7. **Offer the follow-through:** add contact roles for the buying committee, and
   create a first follow-up task.

   ```json
   {
     "sobject-name": "OpportunityContactRole",
     "body": {
       "OpportunityId": "006XXXXXXXXXXXXXXX",
       "ContactId": "003XXXXXXXXXXXXXXX",
       "Role": "Decision Maker",
       "IsPrimary": true
     }
   }
   ```

## Output Format

**Created: Acme — Platform Expansion FY27** — Id `006XXXXXXXXXXXXXXX`

| Field | Value |
|---|---|
| Account | Acme Corporation |
| Stage | Qualification |
| Amount | $250,000 |
| Close date | 2026-12-31 |
| Next step | Discovery workshop scheduled for 2026-09-05 |

Next: add contact roles, or create a follow-up task?

## Handling Edge Cases

- **Derived fields:** Do not write `Probability`, `IsClosed`, `IsWon`, or
  `ForecastCategoryName` — Salesforce derives them from `StageName`.
- **Close date in the past:** Warn the user; a past close date on an open deal
  immediately shows as slipped in every pipeline report.
- **Missing amount:** Allowed, but flag that the deal will be excluded from
  pipeline value roll-ups.
- **Record types:** If the org uses Opportunity record types, the available
  stages depend on the record type. If a stage is rejected, read the schema and
  ask which record type applies, then include `RecordTypeId`.
- **Validation rules:** Relay `FIELD_CUSTOM_VALIDATION_EXCEPTION` messages
  verbatim and ask for the missing values.
- **Products and line items:** `OpportunityLineItem` requires an active price
  book entry. If the user wants products added, explain that a `Pricebook2Id`
  must be set on the opportunity first and confirm the price book with them.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to create that opportunity.
   You should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. On `INSUFFICIENT_ACCESS_OR_READONLY`, explain that their Salesforce profile
   does not permit creating opportunities on that account.
