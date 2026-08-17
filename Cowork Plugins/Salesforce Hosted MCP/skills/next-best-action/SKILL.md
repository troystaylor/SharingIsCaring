---
name: next-best-action
description: |
  Recommends the next concrete action on Salesforce deals based on stage,
  engagement history, and stakeholder coverage. Use when the user asks "what
  should I do next on [deal] in Salesforce", "how do I move this Salesforce
  deal forward", "next best action", "what's my play here", or "help me
  prioritize my Salesforce deals today".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: Lightbulb
---

# Next Best Action

## What This Skill Does

Reads the current state of a Salesforce deal — or the user's whole book — and
recommends the specific next move, grounded in what the CRM actually shows
rather than generic sales advice.

This skill prescribes actions; to find which deals are in trouble first use
`open-risks-and-blockers` or `opportunity-health-summary`.

## When to Activate

- User asks what to do next on a deal or account
- User asks how to advance, unstick, or accelerate a deal
- User asks what to prioritize today or this week

## Workflow

1. **Scope the request.** For a single deal, resolve it with `find`:

   ```
   FIND {Acme Renewal*} IN ALL FIELDS RETURNING Opportunity(Id, Name, Account.Name, StageName, Amount, CloseDate LIMIT 10)
   ```

   For "what should I work on", call `getUserInfo` and use their open deals.

2. **Pull deal state:**

   ```sql
   SELECT Id, Name, Account.Name, AccountId, StageName, Amount, CloseDate,
          Probability, NextStep, Type, LeadSource, ForecastCategoryName,
          Description, CreatedDate, LastActivityDate, LastModifiedDate, Owner.Name
   FROM Opportunity WHERE Id = '006XXXXXXXXXXXXXXX'
   ```

3. **Pull engagement history:**

   ```sql
   SELECT Id, Subject, ActivityDate, Status, IsClosed, TaskSubtype,
          Description, Owner.Name
   FROM Task WHERE WhatId = '006XXXXXXXXXXXXXXX'
   ORDER BY ActivityDate DESC LIMIT 25
   ```

4. **Pull stakeholder coverage:**

   ```sql
   SELECT ContactId, Contact.Name, Contact.Title, Contact.Email, Role, IsPrimary
   FROM OpportunityContactRole WHERE OpportunityId = '006XXXXXXXXXXXXXXX'
   ```

5. **Compare against similar won deals** at the same stage to ground the
   recommendation in what has actually worked:

   ```sql
   SELECT Id, Name, Amount, CloseDate, Type, LeadSource
   FROM Opportunity
   WHERE IsWon = true AND Type = 'New Business' AND CloseDate = LAST_N_DAYS:365
   ORDER BY CloseDate DESC LIMIT 10
   ```

6. **Derive the recommendation** from observed gaps, not templates:

   | Observation | Recommended action |
   |---|---|
   | No contact roles, or only one | Multithread — name who to reach and why |
   | No activity in 21+ days | Re-engage with a specific, value-led reason |
   | `NextStep` blank | Define and record the next step |
   | Close date within 30 days but early stage | Re-baseline the close date or compress the plan |
   | No economic buyer in contact roles | Get access to the budget holder |
   | Long time in one stage | Identify the specific exit criterion that is not met |
   | Open high-priority case on the account | Resolve the service issue before pushing commercially |

7. **Present 3 to 5 ranked actions.** Each must state the action, why (citing
   the CRM evidence), and the expected effect. Offer to write the follow-up
   into Salesforce, which hands off to `update-opportunity` or `log-call-notes`.

## Output Format

**Acme Renewal** — $450K · Negotiation/Review · closes 2026-09-30

**Recommended next actions**

1. **Multithread to Finance.** Only Dana Reyes (VP Engineering) has a contact
   role on this deal and no economic buyer is recorded. Ask Dana for an intro
   to the budget approver this week.
2. **Re-engage — 47 days silent.** Last logged activity was 2026-07-01. Send a
   status recap plus the redline summary.
3. **Set a real next step.** `NextStep` is blank; record "Legal redlines
   returned by 8/24" so the deal is inspectable.

**Evidence** — brief bullets of the CRM facts each recommendation rests on.

## Handling Edge Cases

- **Thin data:** If a deal has no activity and no contact roles, say the CRM has
  too little to reason from and recommend the data-gathering step first.
- **Custom stages:** Read real `StageName` values with `getObjectSchema` before
  labeling a stage early or late.
- **Multiple matching deals:** List them and ask which one, with account, stage,
  amount, and close date to disambiguate.
- **Do not invent history.** Only cite activities, contacts, and dates that
  appear in query results.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to look at that deal. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. Recommendations reflect only the records the signed-in user can see.
