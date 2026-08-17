---
name: delete-salesforce-record
description: |
  Deletes a Salesforce record with strict verification and confirmation. Use
  only when the user explicitly asks to "delete [record] from Salesforce",
  "remove this Salesforce record", "get rid of this duplicate in Salesforce",
  or "purge this test record from the CRM".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: Delete
---

# Delete Salesforce Record

## What This Skill Does

Owns the one destructive path in this plugin. Deletes a Salesforce record only
after the exact record is identified, its dependent data is disclosed, and the
user confirms unambiguously.

## When to Activate

Activate **only** on an explicit deletion request naming a record. Do not
activate for "clean up", "archive", "close out", or "remove from my list" —
those mean status changes, not deletion. When the intent is ambiguous, ask.

## Workflow

1. **Never delete on a name alone.** Resolve the record and show it in full:

   ```
   FIND {"Acme Test Deal"} IN ALL FIELDS RETURNING Opportunity(Id, Name, Account.Name, StageName, Amount, CloseDate, Owner.Name, CreatedDate LIMIT 10)
   ```

   If more than one candidate exists, list them and stop. Ask the user to pick
   by Id.

2. **Read the full record** so the confirmation shows real values:

   ```sql
   SELECT Id, Name, Account.Name, StageName, Amount, CloseDate, Owner.Name,
          CreatedDate, LastModifiedDate
   FROM Opportunity WHERE Id = '006XXXXXXXXXXXXXXX'
   ```

3. **Disclose cascade impact.** Deleting a parent cascades to many children in
   Salesforce. Check what is attached before confirming:

   ```sql
   SELECT COUNT(Id) RelatedTasks FROM Task WHERE WhatId = '006XXXXXXXXXXXXXXX'
   ```

   ```sql
   SELECT COUNT(Id) ContactRoles FROM OpportunityContactRole WHERE OpportunityId = '006XXXXXXXXXXXXXXX'
   ```

   For an Account, the cascade is large — contacts, opportunities, cases, and
   activities all go with it. Count each child object separately; SOQL does not
   allow aggregate functions inside a subquery:

   ```sql
   SELECT COUNT(Id) Contacts FROM Contact WHERE AccountId = '001XXXXXXXXXXXXXXX'
   ```

   ```sql
   SELECT COUNT(Id) Opportunities FROM Opportunity WHERE AccountId = '001XXXXXXXXXXXXXXX'
   ```

   ```sql
   SELECT COUNT(Id) Cases FROM Case WHERE AccountId = '001XXXXXXXXXXXXXXX'
   ```

4. **Consider the safer alternative first.** Say it out loud:
   - Closed Lost instead of deleting an opportunity
   - Merge instead of deleting a duplicate contact or account (merge is not
     available on the standard sObject servers — direct the user to Salesforce,
     unless `tools/list` shows a merge tool from a custom server)
   - Status change instead of deleting a task or case

5. **Require explicit confirmation.** Show a summary and ask the user to
   confirm the record **by name and Id**. A bare "yes" to an unlisted record is
   not sufficient. Never batch-delete on a single confirmation without listing
   every record individually.

6. **Delete** with `deleteSobjectRecord`:

   ```json
   { "sobject-name": "Opportunity", "id": "006XXXXXXXXXXXXXXX" }
   ```

   For a child record reached through a relationship, use
   `deleteRelatedSobjectRecord` with `sobject-name`, `id`, and
   `relationship-path`.

7. **Report the deletion** and state the recovery window: deleted records go to
   the Salesforce Recycle Bin and can be restored there for **15 days**. There
   is no undelete tool on this connector — recovery must happen in Salesforce.

## Output Format

Before deleting:

> **Confirm deletion**
>
> | Field | Value |
> |---|---|
> | Object | Opportunity |
> | Name | Acme Test Deal |
> | Id | 006XXXXXXXXXXXXXXX |
> | Account | Acme Corporation |
> | Stage | Prospecting · $0 · closes 2026-09-30 |
> | Created | 2026-08-01 by Jamie Chen |
>
> **This will also delete:** 3 tasks, 2 contact roles.
>
> Alternative: set the stage to Closed Lost and keep the history.
>
> Reply with the record name to confirm deletion.

After deleting:

**Deleted: Acme Test Deal** (`006XXXXXXXXXXXXXXX`) and 5 related records.
Recoverable from the Salesforce Recycle Bin until 2026-09-01.

## Handling Edge Cases

- **Wrong object type:** Check the Id prefix before deleting — `001` Account,
  `003` Contact, `006` Opportunity, `00Q` Lead, `00T` Task, `500` Case. If the
  prefix does not match the object the user named, stop and clarify.
- **`ENTITY_IS_DELETED`:** The record is already in the Recycle Bin. Say so
  rather than reporting a new deletion.
- **`DELETE_FAILED` / restricted lookup:** Another record references this one
  and blocks the delete. Name the blocking relationship and stop.
- **Cascade too large:** If deleting an account would remove more than a handful
  of children, refuse to proceed in one step. Show the full inventory and ask
  the user to confirm the account deletion explicitly, in writing, again.
- **Any doubt at all:** Do not delete. Ask.

## Handling Authentication

If a tool call fails because the user has not connected to Salesforce yet:

1. Tell the user: "I need to connect to Salesforce first. You should see a
   sign-in prompt — please complete it, and I'll re-verify the record before
   deleting anything."
2. Do NOT retry the delete after sign-in without re-confirming with the user.
   Authentication interruptions are exactly where accidental deletions happen.
3. On `INSUFFICIENT_ACCESS_OR_READONLY`, the signed-in Salesforce user does not
   have delete permission on that object or record. Explain this is an org
   permission and do not attempt a workaround.
