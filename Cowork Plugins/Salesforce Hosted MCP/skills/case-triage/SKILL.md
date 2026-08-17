---
name: case-triage
description: |
  Reviews and triages Salesforce cases — open, escalated, aging, and by
  account. Use when the user asks "what cases are open in Salesforce", "any
  escalations", "support issues at [account]", "triage my Salesforce cases",
  "case backlog", or "is anything blocking this customer".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
cowork.category: Service
cowork.icon: Ticket
---

# Case Triage

## What This Skill Does

Surfaces open Salesforce cases, ranks them by urgency and customer impact, and
connects service issues to the revenue at stake on the same account.

## When to Activate

- User asks about open cases, escalations, or the support backlog
- User asks whether anything is blocking a customer
- User asks for case status on a specific account or contact

## Workflow

1. **Scope the request.** Default to cases owned by the signed-in user
   (`getUserInfo` for the Id). If the user names an account, resolve it with
   `find` and scope by `AccountId`.

2. **Pull open cases:**

   ```sql
   SELECT Id, CaseNumber, Subject, Status, Priority, Origin, Type, Reason,
          IsEscalated, Account.Name, AccountId, Contact.Name, Contact.Email,
          Owner.Name, CreatedDate, LastModifiedDate
   FROM Case
   WHERE IsClosed = false AND OwnerId = '005XXXXXXXXXXXXXXX'
   ORDER BY IsEscalated DESC, CreatedDate ASC LIMIT 100
   ```

3. **Pull cases for a named account:**

   ```sql
   SELECT Id, CaseNumber, Subject, Status, Priority, IsEscalated,
          Contact.Name, Owner.Name, CreatedDate
   FROM Case
   WHERE IsClosed = false AND AccountId = '001XXXXXXXXXXXXXXX'
   ORDER BY CreatedDate ASC LIMIT 50
   ```

4. **Measure aging.** Compute days open from `CreatedDate`. Flag anything open
   longer than 7 days at High priority, or 30 days at any priority.

5. **Correlate with revenue** — a High case on an account with a deal closing
   this quarter is the real priority:

   ```sql
   SELECT Id, Name, AccountId, Account.Name, StageName, Amount, CloseDate
   FROM Opportunity
   WHERE IsClosed = false AND AccountId IN ('001XXXXXXXXXXXXXXX')
   ORDER BY CloseDate ASC LIMIT 25
   ```

6. **Read the case thread** when the user asks about one specific case:

   ```sql
   SELECT Id, CommentBody, CreatedDate, CreatedBy.Name
   FROM CaseComment WHERE ParentId = '500XXXXXXXXXXXXXXX'
   ORDER BY CreatedDate DESC LIMIT 20
   ```

7. **Report** grouped as Escalated / High / Aging / Rest, with the revenue
   correlation called out explicitly.

## Output Format

**Open cases — 17 · 2 escalated · 4 aging**

**Escalated**

| Case | Account | Subject | Priority | Days open | Revenue at stake |
|---|---|---|---|---|---|
| 00012345 | Acme Corp | Login outage | High | 12 | Acme Renewal $450K closes 2026-09-30 |

**High priority** · **Aging (30+ days)** — same table shape.

**Triage recommendation** — 3 to 5 named cases in the order to work them, each
with the reason (escalation, age, revenue exposure, customer tier).

## Handling Edge Cases

- **Custom status and priority values:** Orgs frequently customize these. Read
  real values with `getObjectSchema` and `object-name: Case` before filtering.
- **`CaseComment` visibility:** Comment access can be restricted; if the query
  errors or returns nothing, report the case without the thread rather than
  failing the whole request.
- **Queue-owned cases:** `OwnerId` can be a Queue (Group) rather than a User,
  so an owner-scoped query may miss unassigned work. If the user asks about the
  team backlog, filter by account or omit the owner filter instead.
- **Priority sort order:** `Priority` is a picklist sorted alphabetically, not
  by severity — `High` < `Low` < `Medium` alphabetically. Sort the results
  yourself into High → Medium → Low rather than trusting `ORDER BY Priority`.
- **No open cases:** Say so and offer recently closed cases from the last 30
  days as context.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce to pull those cases. You
   should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If they are already connected and still get an auth error, suggest
   disconnecting and reconnecting the Salesforce connector from Sources &
   Skills.
4. If the org does not use Service Cloud, the Case object may be unavailable —
   say so rather than reporting an empty backlog.
