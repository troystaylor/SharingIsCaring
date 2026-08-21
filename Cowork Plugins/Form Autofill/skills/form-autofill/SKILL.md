---
name: form-autofill
description: |
  Fills out forms that arrive as email attachments (PDF, Word, Excel) using the
  user's profile information, asks the user about anything that can't be filled
  automatically, then returns the completed file to the sender. Use when the user
  says "fill out this form", "complete this paperwork", "someone sent me a form",
  "handle this onboarding packet", or when an email arrives with a fillable
  attachment that needs completing and returning.
metadata:
  author: "Troy Taylor"
  version: "1.0"
  pattern: orchestration
---

# Form Autofill

## What This Skill Does

Takes a form that arrived as an email attachment and returns it completed. Fills
every field that can be resolved from the user's directory profile or their saved
answers, brings the user in for anything else, verifies the result, and replies to
the original sender.

## When to Activate

- An email arrives with a PDF, Word, or Excel attachment that is a form to complete
- The user shares a form file and asks to have it filled out
- The user refers to paperwork, an application, an onboarding packet, or an intake form
- An event-driven task fires on an incoming message matching a form-related trigger

## The Three-Tier Field Model

Every field on every form belongs to exactly one tier. This decides the behavior.

| Tier | Source | Persisted | Behavior |
|------|--------|-----------|----------|
| 1 | Directory profile, fetched live | No | Fill silently |
| 2 | User interview | Yes, in the vault | Fill from vault if fresh, otherwise ask |
| 3 | User, every single time | **Never** | Always ask, never store, never infer |

**Tier 3 is absolute.** Government identifiers, tax IDs, bank and payment details,
passport and visa numbers, health and medical information, and full credentials are
Tier 3. Never fill these from memory, from the vault, from an earlier form in the
same session, or from another attachment. Ask every time, even if the same value
was provided ten minutes ago.

## Workflow

### 1. Triage the attachments

Open every attachment and classify each one:

- **Fillable form** — has blank fields, labeled lines, checkboxes, or form controls
- **Reference material** — instructions, policy documents, cover letters, examples
- **Already complete** — a form with values already present

Only fill the first category. Report the others rather than modifying them. If a
document is ambiguous, ask the user before touching it.

### 2. Enumerate the fields

For each fillable form, build a complete inventory of the fields before filling
anything. Record for each field: the label as printed, the normalized concept it
maps to, the expected format, and whether it is required.

Normalize labels through `references/field-synonyms.md` so that "Surname",
"Family Name", and "Last Name" all resolve to one concept. This matters when a
packet contains several forms that ask for the same information in different words —
the user should be asked once, not once per form.

### 3. Resolve each field to a tier

Look up the user's directory profile for the organizational fields — name, job
title, department, office location, work phone, work email, manager, employee ID.

**If the profile cannot be retrieved, do not fail and do not guess.** Treat those
fields as Tier 2 instead and collect them in the interview. Degrading into more
questions is always correct; inventing a value never is.

Assign everything else to Tier 2 or Tier 3 using the rules above. When a field is
genuinely ambiguous, classify it into the *more* protective tier.

### 4. Fill what is safe to fill

Write Tier 1 values, and Tier 2 values from the vault where they are present and
still fresh. Leave every other field visibly blank.

Never invent, approximate, or infer a value to make a form look finished. A blank
field is a question; a wrong field is a defect that travels to a third party.

### 5. Interview for the gaps

Hand off to `profile-interview` with the consolidated list of unresolved fields
across **all** attachments at once. One batched set of questions, not one per form
and never one per field.

### 6. Apply, then verify

Write the answers into the forms, then **re-read each file and confirm the values
landed in the fields they were meant for.** Do not skip this. Silent write failures
and off-by-one field placement are the most likely defects, and they are invisible
unless checked.

If verification finds a mismatch, correct it and verify again. If it cannot be
corrected, tell the user plainly which field failed rather than sending a form with
misplaced data.

### 7. Return to the sender

Draft a reply to the original sender with the completed files attached. Keep the
body short — confirm what is enclosed, and flag anything left blank and why.

This step requires approval. **Never enable "skip future prompts" for it.** Each
outbound form goes to a third party and deserves its own decision.

### 8. Save what should persist

Hand confirmed Tier 2 answers to `profile-vault`. Tier 3 answers are discarded at
the end of the session and never written anywhere.

## Never Leak Values Into Summaries

When narrating progress, writing a session recap, or drafting the reply body,
refer to fields by **name**, never by value.

- Correct: "Filled date of birth and home address; two fields still need your input."
- Wrong: "Filled date of birth as 1980-01-01."

A recap that quotes values can be forwarded, logged, or pasted somewhere the values
do not belong. This applies to Tier 1 and Tier 2 as much as Tier 3.

## Handling Edge Cases

- **Attachment isn't a form** — report it, don't fill it
- **Scanned or flattened document with no form fields** — say so explicitly and offer
  to produce a filled copy as a new document instead of silently failing
- **Form is already partly filled** — leave existing values alone, fill only blanks,
  and flag anything that looks contradictory
- **Password-protected file** — ask the user for the password, never guess
- **Conflicting values across forms in one packet** — ask, don't pick
- **Sender is external or unrecognized** — flag this before replying; forms going
  outside the organization warrant an extra look
- **Very large packet** — confirm scope with the user before filling more than a
  handful of forms

## Additional Resources

- **`references/field-synonyms.md`** — Label normalization and tier classification
- **`references/form-triage.md`** — How to tell a fillable form from reference material
