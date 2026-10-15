---
name: preview-bulk-update
description: Plan and run a bulk change across many Salesforce records safely - query the exact set, show a preview with counts, get approval, then apply in small batches and report results. Use when asked to update many records at once, such as every overdue opportunity or a list of accounts.
---

# Preview Bulk Update

## When to use this skill

- The user wants the same change applied to multiple records matching a rule or a supplied list.
- Cue phrases: "update every past-due opportunity", "set these five accounts' revenue", "reassign all of Dana's open leads".

## How to do it

1. Restate the rule and the intended field changes. Call `getObjectSchema` for the object to verify field names and valid values.
2. Query the exact target set with `soqlQuery`, including Id, name and the current values of the fields to change. Use a `LIMIT` and aggregate `COUNT()` to report the total matching.
3. Show a preview: the total count, a sample of up to 10 rows with before and after values, and anything excluded and why.
4. Ask for explicit approval of the whole set. If the count is large (more than about 50), propose doing a small batch first.
5. Apply with `updateSobjectRecord`, one record per call, in batches. Stop on the first unexpected error pattern instead of continuing.
6. Report successes, failures with the error text, and any records left unchanged. Offer to re-query to verify.

## What to watch for

- There is no undo and no delete in this plugin; the preview is the safety net, so never skip it.
- Never widen the set after approval. If the query changes, show a new preview.
- Do not update fields the user did not name. Respect validation rules and field-level security.
