---
name: find-and-remove-duplicates
description: Find duplicate Salesforce records such as leads or contacts sharing an email, choose which copy to keep, and delete the rest after approval. Use when asked to deduplicate, find duplicate leads or contacts, or delete older copies.
---

# Find And Remove Duplicates

## When to use this skill

- The user wants duplicate records identified and removed.
- Cue phrases: "find duplicate leads by email", "delete the older copies", "dedupe these contacts".

## How to do it

1. Agree the object, the match key (for example `Email`, or `Name` plus `Company`) and the keep rule (for example newest, oldest, or most complete).
2. Call `getObjectSchema` for the object to confirm the key fields and how to judge completeness.
3. Find duplicate groups with aggregate SOQL, for example `SELECT Email, COUNT(Id) FROM Lead WHERE Email != null GROUP BY Email HAVING COUNT(Id) > 1 LIMIT 200`.
4. For each group, query the member records (Id, name, created date, last activity, owner, key fields) and apply the keep rule. Check for children or open activity on the copies to be deleted.
5. Present a table per group: the record to keep and the records proposed for deletion with the reason, plus total counts. Flag groups where the right keeper is unclear and leave them out.
6. After explicit approval, delete with `deleteSobjectRecord`, one record per call, in small batches. Stop on the first unexpected error.
7. Report records deleted, groups skipped, and any failures.

## What to watch for

- Deleting a duplicate does not merge its data. Tell the user if the copies hold fields or related records the keeper lacks, and suggest merging in Salesforce first.
- Matching on a blank or shared value (for example a generic email) creates false duplicates; exclude those.
- Deleted records stay in the Recycle Bin for 15 days; there is no undelete through MCP.
