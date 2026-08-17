---
name: update-opportunity
description: |
  Updates Salesforce opportunity fields including stage, amount, close date,
  next step, and owner. Use when the user asks "update this Salesforce
  opportunity", "move [deal] to negotiation", "change the amount on [deal]",
  "push the close date in Salesforce", "mark this deal closed won", or
  "reassign this Salesforce deal".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: Edit
---

# Update Opportunity

## What This Skill Does

Changes fields on an existing Salesforce Opportunity — stage progression, value,
timing, next step, or ownership — with a before/after confirmation step.

## When to Activate

- User asks to advance, update, or correct a deal
- User asks to change close date, amount, stage, or next step
- User asks to mark a deal won or lost
- User asks to reassign a deal

## Workflow

1. **Resolve the opportunity:**

   ```
   FIND {"Acme Renewal"} IN ALL FIELDS RETURNING Opportunity(Id, Name, Account.Name, StageName, Amount, CloseDate, Owner.Name LIMIT 10)
   ```

   If several match, show account, stage, amount, and close date and ask which.

2. **Read current values:**

   ```sql
   SELECT Id, Name, Account.Name, StageName, Amount, CloseDate, Probability,
          NextStep, Type, LeadSource, ForecastCategoryName, Description,
          OwnerId, Owner.Name, IsClosed, IsWon
   FROM Opportunity WHERE Id = '006XXXXXXXXXXXXXXX'
   ```

3. **Validate the target stage.** Call `getObjectSchema` with
   `object-name: Opportunity` and confirm the exact `StageName` value. Orgs
   customize stages heavily — a near-miss string will be rejected or, worse,
   accepted into the wrong stage.

4. **Resolve an owner change** to a User Id:

   ```sql
   SELECT Id, Name, Email, IsActive FROM User WHERE Name LIKE '%Chen%' AND IsActive = true LIMIT 10
   ```

5. **Show before/after and get explicit confirmation.** For a closing change
   (Closed Won or Closed Lost), state clearly that the deal will be locked out
   of pipeline reporting and, for Closed Lost, ask for the loss reason if the
   org captures one.

6. **Apply the update** with `updateSobjectRecord`, sending only changed fields:

   ```json
   {
     "sobject-name": "Opportunity",
     "id": "006XXXXXXXXXXXXXXX",
     "body": {
       "StageName": "Negotiation/Review",
       "Amount": 475000,
       "CloseDate": "2026-10-15",
       "NextStep": "Legal redlines returned by 2026-08-24"
     }
   }
   ```

7. **Offer a matching follow-up.** If `StageName` or `NextStep` changed, offer
   to create a task so the next step is actually tracked:

   ```json
   {
     "sobject-name": "Task",
     "body": {
       "Subject": "Follow up: legal redlines",
       "WhatId": "006XXXXXXXXXXXXXXX",
       "ActivityDate": "2026-08-24",
       "Status": "Not Started",
       "Priority": "High"
     }
   }
   ```

8. **Report the before/after summary** and list any task created.

## Output Format

**Updated: Acme Renewal** — Id `006XXXXXXXXXXXXXXX` · Acme Corporation

| Field | Before | After |
|---|---|---|
| Stage | Proposal/Price Quote | Negotiation/Review |
| Amount | $450,000 | $475,000 |
| Close date | 2026-09-30 | 2026-10-15 |
| Next step | — | Legal redlines returned by 2026-08-24 |

Created follow-up task "Follow up: legal redlines" due 2026-08-24.

## Handling Edge Cases

- **Derived fields:** Never write `Probability`, `IsClosed`, `IsWon`, or
  `ForecastCategoryName`. Set `StageName` and let Salesforce derive them. If the
  user wants a probability that differs from the stage default, confirm the org
  allows manual override before attempting it.
- **Closing a deal:** Set `StageName` to the org's closed-won or closed-lost
  value. Confirm the exact string from the schema — it is often not literally
  "Closed Won".
- **Reopening a closed deal:** Possible by setting an open stage, but call out
  that it affects historical forecast snapshots.
- **Slipping a close date:** If the new date moves the deal into another period,
  say so explicitly — it changes the forecast.
- **Validation rules and required-on-stage fields:** Many orgs require fields at
  certain stages. Relay `FIELD_CUSTOM_VALIDATION_EXCEPTION` messages verbatim
  and collect the missing values before retrying.
- **Bulk stage moves:** List every deal that would change, take one explicit
  confirmation, then update one at a time and report per-deal results.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to update that deal. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. On `INSUFFICIENT_ACCESS_OR_READONLY`, the signed-in user lacks edit rights on
   that opportunity — often because they are not the owner and the org's sharing
   model is private.
