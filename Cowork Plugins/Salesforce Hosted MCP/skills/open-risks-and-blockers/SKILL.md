---
name: open-risks-and-blockers
description: |
  Surfaces stalled, slipping, and blocked Salesforce deals plus overdue
  follow-ups and escalated cases. Use when the user asks "what's at risk in
  Salesforce", "which Salesforce deals are stalled", "show me blockers",
  "what's slipping this quarter", "overdue tasks in Salesforce", or "what needs
  my attention in Salesforce".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: Warning
---

# Open Risks and Blockers

## What This Skill Does

Finds everything in Salesforce that is quietly going wrong — deals with no
recent activity, close dates already in the past, overdue tasks, and escalated
cases — and ranks it by value and urgency.

This skill sweeps for problems across the book; for pipeline totals use
`pipeline-review`, and for what to do about one specific deal use
`next-best-action`.

## When to Activate

- User asks what is at risk, stalled, slipping, or blocked
- User asks what needs their attention or what they are behind on
- User asks about overdue follow-ups or escalations

## Workflow

1. **Get the user's identity** with `getUserInfo` and use their User Id as the
   default owner filter. Widen the scope only if the user asks for the team.

2. **Find past-due open deals:**

   ```sql
   SELECT Id, Name, Account.Name, StageName, Amount, CloseDate, NextStep,
          LastActivityDate, Owner.Name
   FROM Opportunity
   WHERE IsClosed = false AND CloseDate < TODAY AND OwnerId = '005XXXXXXXXXXXXXXX'
   ORDER BY Amount DESC NULLS LAST LIMIT 50
   ```

3. **Find stalled deals** (no activity in 21+ days) closing this period:

   ```sql
   SELECT Id, Name, Account.Name, StageName, Amount, CloseDate,
          LastActivityDate, LastModifiedDate
   FROM Opportunity
   WHERE IsClosed = false AND OwnerId = '005XXXXXXXXXXXXXXX'
     AND CloseDate = THIS_FISCAL_QUARTER
     AND (LastActivityDate < LAST_N_DAYS:21 OR LastActivityDate = null)
   ORDER BY Amount DESC NULLS LAST LIMIT 50
   ```

4. **Find deals with no defined next step:**

   ```sql
   SELECT Id, Name, Account.Name, StageName, Amount, CloseDate
   FROM Opportunity
   WHERE IsClosed = false AND OwnerId = '005XXXXXXXXXXXXXXX'
     AND (NextStep = null OR NextStep = '')
   ORDER BY Amount DESC NULLS LAST LIMIT 50
   ```

5. **Find overdue tasks:**

   ```sql
   SELECT Id, Subject, ActivityDate, Priority, Status, WhatId, WhoId
   FROM Task
   WHERE IsClosed = false AND OwnerId = '005XXXXXXXXXXXXXXX'
     AND ActivityDate < TODAY
   ORDER BY ActivityDate ASC LIMIT 50
   ```

6. **Find escalated or aging cases** on the user's accounts:

   ```sql
   SELECT Id, CaseNumber, Subject, Account.Name, Status, Priority,
          IsEscalated, CreatedDate
   FROM Case
   WHERE IsClosed = false AND (IsEscalated = true OR Priority = 'High')
     AND Account.OwnerId = '005XXXXXXXXXXXXXXX'
   ORDER BY CreatedDate ASC LIMIT 25
   ```

7. **Rank and report.** Order by revenue at risk first, then by how overdue the
   item is. Group into "Act today", "This week", and "Watch". Every item must
   name the record and the specific reason it is flagged.

## Output Format

**At risk — $1.4M across 9 items**

**Act today**

| Item | Type | Value | Why |
|---|---|---|---|
| Acme Renewal | Opportunity | $450K | Close date 33 days past due, no activity in 47 days |
| Case 00012345 — Login outage | Case | Acme Corp | Escalated, open 12 days |

**This week** — same table shape.

**Watch** — same table shape, lower urgency.

**Suggested next actions** — 3 to 5 named actions, each tied to a record above.
Offer to create follow-up tasks or update close dates, which hands off to the
`log-call-notes` or `update-opportunity` skills.

## Handling Edge Cases

- **Null date comparisons:** `LastActivityDate < LAST_N_DAYS:21` excludes null
  values, so include `OR LastActivityDate = null` to catch deals with no
  activity at all. In SOQL, use `= null`, not `IS NULL`.
- **Empty string vs null:** Text fields can be either. Check both, as shown for
  `NextStep`.
- **Nothing at risk:** Say so directly and show what is closing soon instead —
  do not manufacture risks.
- **Too many results:** Cap at the top 10 per category by value and state the
  total count.
- **Team scope:** For a manager view, replace the owner filter with
  `Owner.ManagerId = '005XXXXXXXXXXXXXXX'` and group findings by rep.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to check what's at risk. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. Risk detection only covers records the signed-in user can see in Salesforce.
