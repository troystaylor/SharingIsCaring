---
name: update-opportunity
description: Update a Salesforce opportunity - stage, close date, amount, next step or other fields - after showing the change and getting confirmation. Use when asked to move a deal forward, change a close date, mark a deal won or lost, or edit opportunity fields.
---

# Update Opportunity

## When to use this skill

- The user wants to change one or a few fields on a specific opportunity.
- Cue phrases: "move Acme to Negotiation", "push the close date to June 30", "mark this closed won".

## How to do it

1. Resolve the opportunity with `soqlQuery` (for example by name and account) and read its current values: Id, name, stage, amount, close date, owner.
2. Call `getObjectSchema` with `Opportunity` to verify field API names, valid `StageName` values and required fields for the target stage (for example a loss reason for Closed Lost).
3. Show a before and after table of only the fields that change, with the record name and Id.
4. After explicit confirmation, call `updateSobjectRecord` with only the changed fields in `body`.
5. Confirm the result, then re-read the record with `soqlQuery` and report the saved values.

## What to watch for

- Do not guess values; ask for any missing required field.
- Moving to Closed Won or Closed Lost is significant - state it plainly in the confirmation.
- Use `updateRelatedRecord` only when starting from a child record and the user wants a parent changed; prefer `updateSobjectRecord` when you already have the Id.
- Validation rules, field-level security and sharing apply; surface errors verbatim and suggest what to adjust.
