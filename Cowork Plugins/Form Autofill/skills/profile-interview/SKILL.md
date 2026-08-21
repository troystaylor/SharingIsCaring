---
name: profile-interview
description: |
  Collects missing personal information from the user in a single batched,
  well-organized set of questions rather than one field at a time. Use when a form,
  application, or profile needs details the agent doesn't have, when filling out
  paperwork requires input from the user, or when confirming and correcting
  previously supplied personal details.
metadata:
  author: "Troy Taylor"
  version: "1.0"
  pattern: elicitation
---

# Profile Interview

## What This Skill Does

Asks the user for personal information they need to supply, in a way that feels
like completing a short form rather than being interrogated. Stores nothing itself —
it collects, validates, and hands the answers back to whatever asked for them.

## When to Activate

- A form has fields that can't be filled from available sources
- Previously supplied details need confirming because they may be stale
- The user is correcting or updating their own information
- Any workflow needs personal details the agent doesn't already have

## Core Principle: Ask Once, Ask Well

The failure mode this skill exists to prevent is field-by-field questioning. Asking
"What's your date of birth?", waiting, then "And your address?", is exhausting, and
in a background or event-driven run each round trip may sit unanswered for hours.

**Collect every outstanding field across every document, then ask once.**

## Workflow

### 1. Assemble the full question set

Before asking anything, gather every unresolved field across all documents in scope.
Deduplicate by canonical concept so a detail requested by three forms is asked once.

### 2. Group into themes

Present questions grouped, in this order. Familiar shape makes it faster to answer.

1. **Identity** — legal name, preferred name, date of birth, nationality
2. **Contact** — home address, personal phone, personal email
3. **Emergency contact** — name, relationship, phone
4. **Preferences** — dietary, accessibility, sizes
5. **Sensitive** — always last, always explained

### 3. Explain the why

State which document wants each field and what it's for. A user who understands the
purpose answers faster and is better placed to refuse when refusing is right.

> The new starter form needs your home address for payroll records.

### 4. Mark what's optional

Distinguish required from optional fields explicitly. Never present an optional field
as though the form can't proceed without it.

### 5. Handle sensitive fields separately

Ask for sensitive details — government identifiers, tax numbers, bank details,
passport numbers, health information — in a clearly marked final group, with:

- Which form requires it and for what purpose
- A statement that the value **will not be saved** and will be asked again next time
- An explicit offer to leave it blank and complete that part personally

Never bundle a sensitive field quietly among ordinary ones.

### 6. Accept refusal gracefully

"Skip", "prefer not to", "leave it blank", or silence are all valid answers. Record
the field as declined, leave it blank, note it in the summary, and **move on**. Do
not re-ask, do not rephrase and try again, do not explain why it would be better to
answer. A user declining to share their SSN with an automated process is behaving
sensibly.

### 7. Normalize and validate on the way in

- **Dates** — resolve ambiguity explicitly. `03/04/2026` is March 4th or April 3rd
  depending on locale; ask rather than assume. Store ISO 8601.
- **Phone numbers** — capture country code, store E.164
- **Addresses** — capture structured parts, not one blob
- **Names** — capture parts separately, and ask whether legal and preferred differ
- **Obvious errors** — a birth date in the future or an implausible postcode gets
  queried, gently, once

### 8. Confirm before use

Echo back what was captured, grouped as it was asked, and get confirmation before
anything is written into a document or saved. This is the user's chance to catch a
typo before it reaches a third party.

Show sensitive values **masked** in the confirmation — last four characters only.

## Question Format

Present as a compact list the user can answer in one message:

> I need a few details to finish the new starter form. Required unless marked optional.
>
> **Identity**
> 1. Legal name as it appears on your ID
> 2. Date of birth
>
> **Contact**
> 3. Home address
> 4. Personal mobile number
>
> **Emergency contact**
> 5. Name and relationship
> 6. Contact phone number
>
> **Preferences** *(optional)*
> 7. Dietary requirements
>
> **Sensitive** — not saved, asked fresh each time. Say "skip" to leave blank and
> complete it yourself.
> 8. National Insurance number — required by the form for payroll

Number the questions so the user can answer by number, and accept partial answers
without restarting the whole set.

## Handling Edge Cases

- **Partial answers** — accept them, thank the user, list only what's still open
- **Answers out of order or in prose** — parse them; don't insist on a format
- **"Use what you have on file"** — confirm what's on file and its age, don't assume
- **User doesn't know a value** — offer to leave it blank and flag it for follow-up
- **A form for someone else** — establish whose details are needed before asking
- **User pastes a document containing the details** — extract, then confirm what
  was extracted rather than using it silently

## What This Skill Must Never Do

- Ask for a sensitive value that isn't actually required by a document in scope
- Retain a sensitive value beyond the immediate task
- Pressure, repeat, or rephrase a declined question
- Guess or infer a personal detail from context
- Echo an unmasked sensitive value back into the conversation
- Include captured values in a session summary or an outbound message body

## Additional Resources

- **`references/question-patterns.md`** — Worked phrasings for common field types
