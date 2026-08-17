---
name: pipeline-review
description: |
  Rolls up Salesforce pipeline and forecast by stage, owner, close period, or
  account. Use when the user asks "what's my Salesforce pipeline", "pipeline by
  stage", "how much closes this quarter in Salesforce", "forecast from
  Salesforce", "team pipeline in Salesforce", or "how are we tracking to
  number".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: aggregation
cowork.category: Sales
cowork.icon: ChartBar
---

# Pipeline Review

## What This Skill Does

Produces pipeline and forecast roll-ups from Salesforce opportunity data —
totals by stage, close period, owner, forecast category, or account — plus
period-over-period movement.

This skill answers "how much"; for deal-by-deal hygiene use
`opportunity-health-summary`, and for what is slipping use
`open-risks-and-blockers`.

## When to Activate

- User asks for pipeline totals, coverage, or forecast
- User asks how much is closing in a period
- User asks how the team or a rep is tracking

## Workflow

1. **Establish scope and period.** Default to the signed-in user's open deals
   in the current fiscal quarter. Call `getUserInfo` for their User Id. Ask
   only if the request is genuinely ambiguous (for example "the team" without
   naming one).

2. **Roll up by stage** using a SOQL aggregate — this avoids pulling hundreds
   of rows:

   ```sql
   SELECT StageName, COUNT(Id) DealCount, SUM(Amount) TotalAmount
   FROM Opportunity
   WHERE IsClosed = false AND OwnerId = '005XXXXXXXXXXXXXXX'
     AND CloseDate = THIS_FISCAL_QUARTER
   GROUP BY StageName ORDER BY SUM(Amount) DESC
   ```

3. **Roll up by forecast category:**

   ```sql
   SELECT ForecastCategoryName, COUNT(Id) DealCount, SUM(Amount) TotalAmount
   FROM Opportunity
   WHERE IsClosed = false AND OwnerId = '005XXXXXXXXXXXXXXX'
     AND CloseDate = THIS_FISCAL_QUARTER
   GROUP BY ForecastCategoryName
   ```

4. **Pull closed-won to date** for attainment context:

   ```sql
   SELECT COUNT(Id) DealCount, SUM(Amount) TotalAmount
   FROM Opportunity
   WHERE IsWon = true AND OwnerId = '005XXXXXXXXXXXXXXX'
     AND CloseDate = THIS_FISCAL_QUARTER
   ```

5. **Roll up by owner** when the scope is a team. Use the manager path:

   ```sql
   SELECT Owner.Name, COUNT(Id) DealCount, SUM(Amount) TotalAmount
   FROM Opportunity
   WHERE IsClosed = false AND CloseDate = THIS_FISCAL_QUARTER
     AND Owner.ManagerId = '005XXXXXXXXXXXXXXX'
   GROUP BY Owner.Name ORDER BY SUM(Amount) DESC
   ```

6. **List the largest deals** so the roll-up is actionable:

   ```sql
   SELECT Id, Name, Account.Name, StageName, Amount, CloseDate, NextStep, Owner.Name
   FROM Opportunity
   WHERE IsClosed = false AND CloseDate = THIS_FISCAL_QUARTER
     AND OwnerId = '005XXXXXXXXXXXXXXX'
   ORDER BY Amount DESC NULLS LAST LIMIT 15
   ```

7. **Report** totals, distribution, concentration risk, and the deals that
   carry the period.

## Output Format

**Pipeline — current fiscal quarter · Jamie Chen**

- Open pipeline: **$3.4M** across 22 deals · average deal $155K
- Closed won to date: **$1.1M** across 6 deals
- Commit + Best Case: **$1.9M**
- Top 3 deals are **48%** of open pipeline — concentration risk

| Stage | Deals | Amount | % of pipeline |
|---|---|---|---|
| Negotiation/Review | 4 | $1,200,000 | 35% |
| Proposal/Price Quote | 6 | $900,000 | 26% |

**Largest open deals**

| Opportunity | Account | Stage | Amount | Close |
|---|---|---|---|---|
| Acme Renewal | Acme Corp | Negotiation | $450,000 | 2026-09-30 |

**Observations** — 3 to 4 bullets: coverage, concentration, slippage risk,
stage bunching.

## Handling Edge Cases

- **Aggregate aliases:** SOQL aggregate results come back as `expr0`, `expr1`
  unless you alias them (as above). Always alias so the output is readable.
- **`GROUP BY` restrictions:** You cannot `ORDER BY` a field that is not
  grouped or aggregated. Order by the aggregate, as shown.
- **Fiscal vs calendar:** `THIS_FISCAL_QUARTER` follows the org's fiscal
  calendar; `THIS_QUARTER` is calendar-based. Say which one you used.
- **Null amounts:** Deals with no `Amount` are excluded from `SUM`. Count them
  separately and disclose the number so totals are not silently understated:
  `WHERE IsClosed = false AND Amount = null`.
- **Multi-currency orgs:** If `CurrencyIsoCode` exists, aggregate on
  `ConvertedAmount` and state the corporate currency.
- **Quota data:** Salesforce quota objects are not reliably queryable across
  orgs. If the user asks about attainment percentage, ask for the quota number
  rather than guessing.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to build that pipeline view.
   You should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. Roll-ups only include records the signed-in user can see. For team views,
   note that their Salesforce role hierarchy determines visibility.
