---
name: renewal-review
description: Review upcoming Salesforce renewals - contracts, renewal opportunities and subscriptions ending soon - and flag risk using activity, open cases and stage. Use when asked what is up for renewal, which renewals are at risk, or to prepare a renewal review.
---

# Renewal Review

## When to use this skill

- The user wants to see renewals coming due and which ones need attention.
- Cue phrases: "what renews next quarter", "renewals at risk", "prep for the renewal of Acme".

## How to do it

1. Call `getObjectSchema` index mode to learn how this org models renewals: `Contract`, renewal `Opportunity` (a `Type` such as Renewal), or a custom subscription object. Confirm field names in detail mode.
2. Call `getUserInfo` for "my" renewals; otherwise ask for the owner, region or time window (default the next 90 days).
3. Query upcoming renewals with `soqlQuery`, for example open opportunities with `Type = 'Renewal'` and a `CloseDate` in the window, or contracts with `EndDate` in the window, including account, owner, amount and stage.
4. For each, add risk signals: last activity date, open high-priority cases (`soqlQuery` on `Case` by account), recent stage or amount changes, and whether a renewal opportunity exists at all.
5. Present a table ordered by end date with value, owner, risk signals and a suggested next step. Highlight contracts ending soon with no renewal opportunity.

## What to watch for

- Do not assume the data model; use what `getObjectSchema` shows and say which objects you used.
- Aggregate totals with SOQL rather than summing a truncated list, and state the currency.
- This skill is read-only. To create or update renewal opportunities, use the Salesforce SObject Mutations or SObject All plugin.
