---
name: profile-vault
description: |
  Securely stores and retrieves the user's saved personal details so forms and
  applications don't ask for the same information repeatedly. Use when saving
  confirmed personal details for reuse, retrieving previously saved details,
  reviewing or correcting what's stored, or deleting saved information.
metadata:
  author: "Troy Taylor"
  version: "1.0"
  pattern: persistence
---

# Profile Vault

## What This Skill Does

Keeps a small, user-owned store of personal details that forms ask for repeatedly,
so the user answers once instead of every time. Holds only details that are safe to
persist — sensitive identifiers are deliberately excluded and always asked fresh.

## When to Activate

- Confirmed personal details should be saved for reuse
- A form needs details the user may have already supplied previously
- The user asks what's stored, or wants to correct or delete it

## Location

The vault lives at **`/Documents/Personal/form-profile.md`** in the user's OneDrive.

**It must never be placed inside the skills folder.** Skills can be shared with
other people; a vault stored alongside one would be shared with it, silently
exposing the user's personal details. Keeping the data outside the skill means
sharing the skill shares the protocol and nothing else.

Never copy, duplicate, back up, or attach the vault file.

## What Goes In, What Stays Out

### Store — Tier 2

Details the user supplied that are reused across forms and not dangerous if exposed:
legal and preferred name, date of birth, home and mailing address, personal phone
and email, emergency contact details, nationality, dietary requirements,
accessibility needs, sizes.

### Never store — Tier 3

Government identifiers, tax numbers, passport and visa numbers, driver's licence
numbers, bank and payment card details, health and medical information, credentials,
and signatures.

**This is not a default to be overridden.** If the user asks to save one of these
for convenience, decline and explain: these values are asked fresh each time
specifically so a stored copy can't be exposed. Offer to make the asking faster
instead.

### Don't store — Tier 1

Directory profile values — job title, department, office, manager, work contacts —
are fetched live. Caching them creates a second source of truth that silently goes
stale. Leave them out.

## File Format

Plain Markdown, so the user can read and edit it directly.

```markdown
# Form Profile

Personal details saved for reuse when filling out forms.
Sensitive identifiers are deliberately excluded — see "Never Stored" below.

## Identity

| Field | Value | Source | Confirmed |
|---|---|---|---|
| Legal name | Alexandra Jane Okafor | interview | 2026-08-21 |
| Preferred name | Alex | interview | 2026-08-21 |
| Date of birth | 1990-03-14 | interview | 2026-08-21 |

## Contact

| Field | Value | Source | Confirmed |
|---|---|---|---|
| Home address | 12 Oak Lane, Bristol, BS1 4TR, United Kingdom | interview | 2026-08-21 |
| Personal mobile | +44 7700 900123 | interview | 2026-08-21 |

## Emergency Contact

| Field | Value | Source | Confirmed |
|---|---|---|---|
| Name | Daniel Okafor | interview | 2026-08-21 |
| Relationship | Brother | interview | 2026-08-21 |
| Phone | +44 7700 900456 | interview | 2026-08-21 |

## Never Stored

These are deliberately absent and are asked fresh every time:
national ID, tax ID, passport number, visa number, driver's licence,
bank details, payment cards, health information, credentials, signature.
```

The **Never Stored** section stays in the file permanently. It records intent, so a
later session doesn't helpfully fill the gap.

## First Use — Consent

Do not create the vault silently. On first use, explain what it will hold, where it
lives, what it will never hold, and that it can be deleted at any time. Create it
only on agreement. Create the `/Documents/Personal/` folder too if it doesn't exist.

If the user declines, that is a complete answer. Proceed without a vault, collect
the details through the interview for this task only, and do not offer again in the
same session. Everything still works — the user is simply asked each time.

Then ask the user to apply a **Confidential sensitivity label** to the file. This
brings the file under organizational data-loss-prevention and audit controls, rather
than relying on this skill's own good behavior.

## Reading

1. Read the file at the point of need, not speculatively
2. Return only the fields actually required for the task at hand
3. Check the `Confirmed` date and apply the freshness rules below
4. If the file doesn't exist, say so and proceed to interview — this is normal, not
   an error

## Writing

Follow this sequence exactly. The ordering matters.

1. **Re-read the file immediately before writing.** Event-driven tasks can run
   concurrently; a value read at the start of a long task may be stale by the end.
   Never write based on a copy read earlier in the session.
2. **Write only fields the user explicitly confirmed in this session.** Never
   backfill, never infer, never carry a value over from a document.
3. **Show a diff and get approval** — old value, new value, per field. This is a
   separate approval from any other action in the task.
4. **Never delete silently.** Replacing a value is a change the user should see.
5. **Stamp `Confirmed` with today's date on every confirmation,** including when the
   value is unchanged. Confirming that a value is still correct is itself useful
   information.
6. **Re-read after writing to verify** the file is intact and the values landed.

If the file changed between step 1 and step 3, stop and re-merge rather than
overwriting.

## Freshness

| Field type | Re-confirm after | Always re-confirm when |
|---|---|---|
| Home and mailing address | 12 months | Form is legal, financial, or government |
| Personal phone and email | 12 months | Form is legal, financial, or government |
| Emergency contact | 12 months | Form relates to travel, safety, or medical |
| Legal name | 24 months | Form is legal, financial, or government |
| Date of birth, nationality | Never | — |
| Preferences | 12 months | — |

Values past their window are **suggested, not filled** — present them for
confirmation rather than writing them in.

## Never Leak Vault Contents

Vault values may be written into the form field they were retrieved for, and
nowhere else.

Never place them in a session summary, a progress narration, an email body, a
chat message, a file name, a log, or any output document other than the target
form. When narrating, refer to fields by **name**, never by value.

Never read the vault to answer a general question about the user. It exists to fill
forms, not to describe its owner.

## Correction and Deletion

The user owns this file and their instructions about it are final.

- **"What do you have on me?"** — show the contents, grouped, with confirmation dates
- **"That's wrong"** — correct it immediately, no justification needed
- **"Delete X"** — remove that field, confirm it's gone
- **"Forget everything" / "delete my profile"** — delete the entire file, confirm
  plainly. Do not argue, do not offer to keep part of it, do not retain a copy.

## Handling Edge Cases

- **File is corrupted or unparseable** — do not overwrite. Tell the user, show what
  can be read, and let them decide.
- **Duplicate or conflicting entries** — surface both with their dates and ask
- **A sensitive value is found in the file** — flag it, offer to remove it, and do
  not use it. It should never have been written; treat its presence as a defect.
- **Vault details would be used for someone other than the user** — refuse. This
  vault holds one person's details and fills forms for that person only.
