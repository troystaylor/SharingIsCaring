---
name: review-tasks
description: |
  Lists and triages open Salesforce tasks, follow-ups, and upcoming meetings.
  Use when the user asks "what are my Salesforce tasks", "what's due in
  Salesforce", "my follow-ups", "what meetings do I have on this account",
  "overdue activities in Salesforce", or "what's on my Salesforce list today".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Sales
cowork.icon: CheckboxChecked
---

# Review Tasks

## What This Skill Does

Pulls the signed-in user's open Salesforce activities — tasks and events —
groups them by urgency, and connects each one to the account or deal it belongs
to so the list is prioritizable rather than just long.

This skill covers logged activities; for deal-level priorities use
`next-best-action`, and for stalled deals use `open-risks-and-blockers`.

## When to Activate

- User asks what tasks, follow-ups, or activities are open or due
- User asks what is overdue in Salesforce
- User asks about upcoming meetings on an account or deal

## Workflow

1. **Get the user's Id** with `getUserInfo`.

2. **Pull open tasks:**

   ```sql
   SELECT Id, Subject, ActivityDate, Status, Priority, TaskSubtype,
          WhoId, Who.Name, WhatId, What.Name, AccountId, Description
   FROM Task
   WHERE IsClosed = false AND OwnerId = '005XXXXXXXXXXXXXXX'
   ORDER BY ActivityDate ASC NULLS LAST LIMIT 100
   ```

3. **Pull upcoming meetings:**

   ```sql
   SELECT Id, Subject, StartDateTime, EndDateTime, Location,
          WhoId, Who.Name, WhatId, What.Name
   FROM Event
   WHERE OwnerId = '005XXXXXXXXXXXXXXX' AND StartDateTime >= TODAY
   ORDER BY StartDateTime ASC LIMIT 25
   ```

4. **Scope to a record** when the user names an account or deal:

   ```sql
   SELECT Id, Subject, ActivityDate, Status, Priority, Owner.Name
   FROM Task
   WHERE IsClosed = false AND WhatId = '006XXXXXXXXXXXXXXX'
   ORDER BY ActivityDate ASC NULLS LAST LIMIT 50
   ```

5. **Enrich with deal value** so priority reflects revenue, not just dates.
   Collect the Opportunity Ids from `WhatId` values starting with `006` and
   query them:

   ```sql
   SELECT Id, Name, Amount, StageName, CloseDate
   FROM Opportunity WHERE Id IN ('006XXXXXXXXXXXXXXX')
   ```

6. **Group and report** as Overdue / Today / This week / Later / No due date.
   Within each group, order by linked deal value, then priority.

7. **Offer to act** — closing a task, rescheduling, or adding a follow-up hands
   off to `log-call-notes` (create) or a direct `updateSobjectRecord` call. Any
   write requires explicit confirmation first.

## Output Format

**Your Salesforce activities — 14 open**

**Overdue (4)**

| Due | Task | Related to | Priority |
|---|---|---|---|
| 2026-08-08 | Send redline summary | Acme Renewal ($450K) | High |

**Today (2)** · **This week (5)** · **Later (2)** · **No due date (1)** —
same table shape.

**Upcoming meetings**

| When | Subject | With | Related to |
|---|---|---|---|
| 2026-08-18 10:00 | Acme QBR | Dana Reyes | Acme Corp |

**Suggested order of attack** — 3 items, highest revenue impact first.

## Handling Edge Cases

- **`Who` and `What` polymorphic fields:** `WhoId` points at a Contact or Lead
  and `WhatId` at an Account, Opportunity, Case, or custom object. Use the Id
  prefix to tell them apart (`006` = Opportunity, `001` = Account, `500` =
  Case, `003` = Contact, `00Q` = Lead). `Who.Name` and `What.Name` are
  queryable; other fields on those polymorphic relationships are not.
- **Tasks with no due date:** `ActivityDate` can be null. `ORDER BY
  ActivityDate ASC NULLS LAST` keeps them from crowding the top.
- **Closing a task:** Set `Status` to `Completed` with `updateSobjectRecord`;
  `IsClosed` is derived and cannot be written directly.
- **Nothing open:** Say so and offer to show recently completed work or what is
  coming up next week.
- **Recurring tasks:** Orgs using recurrence may return many near-identical
  rows; collapse them in the summary and note the recurrence.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to pull your activities. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. Only activities the signed-in user owns or can see in Salesforce appear here.
