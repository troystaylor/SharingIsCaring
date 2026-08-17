---
name: opportunity-health-summary
description: |
  Assesses the health of Salesforce opportunities — stage progression, deal
  hygiene, slipped close dates, and missing next steps. Use when the user asks
  "how healthy is this Salesforce deal", "review my Salesforce opportunities",
  "is [opportunity] on track", "deal inspection in Salesforce", "deal hygiene
  check", or "which Salesforce deals have bad data". For pipeline totals or
  forecast amounts, use pipeline-review instead.
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: aggregation
cowork.category: Sales
cowork.icon: ChartLine
---

# Opportunity Health Summary

## What This Skill Does

Scores open Salesforce opportunities on deal hygiene and momentum, then
explains what specifically is wrong with each weak deal. This is inspection,
not forecasting — see `pipeline-review` for value roll-ups.

## When to Activate

- User asks whether a deal or set of deals is healthy or on track
- User asks for a deal inspection, hygiene check, or quality review
- User asks what is wrong with their pipeline

## Workflow

1. **Establish scope.** Default to the signed-in user's own open deals. Call
   `getUserInfo` to get their User Id, then filter on `OwnerId`. If the user
   names an account, team, or single deal, scope to that instead.

2. **Pull the deals** with `soqlQuery`:

   ```sql
   SELECT Id, Name, Account.Name, StageName, Amount, CloseDate, Probability,
          NextStep, ForecastCategoryName, Type, LeadSource, Owner.Name,
          CreatedDate, LastModifiedDate, LastActivityDate
   FROM Opportunity
   WHERE IsClosed = false AND OwnerId = '005XXXXXXXXXXXXXXX'
   ORDER BY CloseDate ASC LIMIT 100
   ```

3. **Pull engagement signals** for the deals in scope:

   ```sql
   SELECT Id, WhatId, Subject, ActivityDate, Status, IsClosed
   FROM Task
   WHERE WhatId IN ('006XXXXXXXXXXXXXXX', '006YYYYYYYYYYYYYYY')
   ORDER BY ActivityDate DESC LIMIT 200
   ```

4. **Check stakeholder coverage** on the important deals:

   ```sql
   SELECT OpportunityId, Contact.Name, Contact.Title, Role, IsPrimary
   FROM OpportunityContactRole
   WHERE OpportunityId IN ('006XXXXXXXXXXXXXXX')
   ```

5. **Score each deal** against these hygiene rules:

   | Signal | Flag when |
   |---|---|
   | Missing next step | `NextStep` is blank |
   | Missing amount | `Amount` is null or 0 |
   | Past-due close date | `CloseDate` < today and still open |
   | Stalled | `LastActivityDate` older than 21 days |
   | No recent edit | `LastModifiedDate` older than 30 days |
   | Single-threaded | Fewer than 2 `OpportunityContactRole` records |
   | Stage/date mismatch | Early stage but `CloseDate` within 30 days |
   | Forecast mismatch | `ForecastCategoryName` = Commit but stage is early |
   | Age | Open longer than typical for the org's sales cycle |

6. **Rate each deal** Healthy / Needs attention / At risk, based on flag count
   and weight (past-due close date and stalled activity weigh most).

7. **Report** with the format below and offer to fix what is fixable — for
   example, "I can add the missing next steps if you tell me what they are"
   (which hands off to the `update-opportunity` skill).

## Output Format

**Deal health — 12 open opportunities, $3.4M**

| Rating | Count | Value |
|---|---|---|
| Healthy | 5 | $1.6M |
| Needs attention | 4 | $1.1M |
| At risk | 3 | $700K |

**At risk**

| Opportunity | Amount | Close | Issues |
|---|---|---|---|
| Acme Renewal | $450K | 2026-07-15 (past due) | Close date 33 days past due · no activity in 47 days · no next step |

**Needs attention** — same table shape.

**Top fixes** — 3 to 5 specific, named actions ranked by value at risk.

## Handling Edge Cases

- **Custom stage names:** Do not assume the default picklist. Call
  `getObjectSchema` with `object-name: Opportunity` to read actual
  `StageName` values before classifying stages as early or late.
- **Large pipelines:** SOQL returns at most 50,000 records per transaction, but
  keep `LIMIT` at 200 or lower and page by close date range for readability.
- **Amounts in mixed currencies:** If the org has multi-currency, the schema
  includes `CurrencyIsoCode` and `ConvertedAmount`. Roll up on
  `ConvertedAmount` when it exists and say which currency you reported in.
- **No activity data:** Some orgs restrict Task visibility. If activity queries
  return nothing across all deals, say that engagement signals were unavailable
  rather than reporting every deal as stalled.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to inspect those deals. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. Results reflect the signed-in user's Salesforce permissions — if the deal
   count looks low, their sharing rules may be limiting visibility.
