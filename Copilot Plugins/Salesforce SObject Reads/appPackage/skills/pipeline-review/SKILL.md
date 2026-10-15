---
name: pipeline-review
description: Review the Salesforce sales pipeline - open opportunities by stage, owner, amount and close date, with past-due and at-risk deals called out. Use when asked about pipeline, forecast, deals closing this quarter, or stalled opportunities.
---

# Pipeline Review

## When to use this skill

- The user asks about pipeline health, deals closing soon, biggest deals, or stalled opportunities.
- Cue phrases: "show my pipeline", "what closes this month", "which deals are at risk", "top opportunities in <industry>".

## How to do it

1. Call `getObjectSchema` with `Opportunity` to confirm the amount field the admin wants used (for example a custom ACV field instead of `Amount`) and the valid `StageName` values.
2. Call `getUserInfo` when the request is about "my" pipeline, and filter on `OwnerId`. For a team view, ask which owners, region or industry to include.
3. Query with `soqlQuery`, for example:
   `SELECT Id, Name, Account.Name, StageName, Amount, CloseDate, Owner.Name, LastActivityDate FROM Opportunity WHERE IsClosed = false AND CloseDate <= NEXT_N_DAYS:90 ORDER BY Amount DESC LIMIT 50`
4. Summarize: total open amount, count and amount by stage, and by owner when relevant.
5. Flag risks: close date in the past, no activity for 30+ days, large deals in early stages, deals closing this month still before negotiation.
6. End with a short list of the deals most worth attention and why.

## What to watch for

- Use aggregate SOQL (`GROUP BY StageName`) for totals rather than summing a truncated list.
- State the filters and currency used; do not mix currencies without saying so.
- This skill is read-only. To change stages or close dates, use the Salesforce SObject Mutations plugin.
