# Form Autofill for Copilot Cowork

Turns emailed forms into completed forms. When a message arrives with a PDF, Word,
or Excel attachment that needs filling out, this plugin triages the attachments,
fills what it can resolve, asks you about the rest in one batch, verifies the
result, and replies to the sender for your approval.

## Skills

| Skill | Role |
|---|---|
| `form-autofill` | Orchestrates triage → fill → verify → reply |
| `profile-interview` | Collects missing details in one batched, well-organized ask |
| `profile-vault` | Persists reusable answers so you aren't asked twice |

## The Three-Tier Field Model

Every form field lands in exactly one tier, and the tier decides the behavior.

| Tier | Source | Persisted | Behavior |
|---|---|---|---|
| 1 | Directory profile, fetched live | No | Filled silently |
| 2 | You, once | Yes, in the vault | Filled from vault if fresh |
| 3 | You, every time | **Never** | Always asked, never stored |

Tier 3 covers government identifiers, tax numbers, bank details, passports, health
information, credentials, and signatures. The friction is deliberate.

Tier 1 values are never cached — caching would create a second source of truth that
silently goes stale. If the directory profile can't be retrieved, those fields
degrade into Tier 2 and get asked instead of guessed.

## The Vault

Saved answers live at **`/Documents/Personal/form-profile.md`** in your OneDrive —
deliberately *outside* the skills folder.

This matters: Cowork lets you share skills with colleagues. A vault stored inside a
skill folder would be shared along with it. Keeping the data separate means sharing
the skill shares the protocol and nothing else.

Apply a **Confidential sensitivity label** to that file once created. Purview
governs Cowork, so the label gives you real DLP enforcement and audit rather than
relying on the skills' own good behavior.

You can read and edit the file directly — it's plain Markdown — and you can ask
Cowork to show, correct, or delete its contents at any time.

## Install

### As personal skills

Copy the three folders under `skills/` into your OneDrive at
`/Documents/Cowork/skills/`. Cowork discovers them at the start of your next session.

### As a plugin

Package it:

```powershell
..\..\'Cowork Plugin Template'\package.ps1
```

Icons are already included — `color.png` (192×192) and `outline.png` (32×32),
rendered from the Fluent UI System Icons **Form** glyph. During development,
`-SkipIcons` lets you package without them.

## Trigger It Automatically

Set up an event-driven task in Cowork:

> When I receive an email with a PDF, Word, or Excel attachment that looks like a
> form to fill out, triage the attachments, fill what you can, ask me about the
> rest, and draft a reply back to the sender.

Event-driven tasks default to draft-and-approve, so the reply waits for you.

**Choose "run in current conversation" if you want answers to persist between
forms** — a new conversation each time loses session context, though the vault
covers most of that gap.

## Design Notes

**One batched ask, never field-by-field.** In a background run, each round trip can
sit unanswered for hours. All outstanding fields across all attachments are
collected and asked once.

**Blank beats wrong.** Nothing is inferred or approximated to make a form look
finished. A blank field is a question; a wrong field is a defect that travels to a
third party.

**Verification is mandatory.** Files are re-read after filling to confirm values
landed in the right fields. Silent write failures and off-by-one placement are the
likeliest defects and are invisible unless checked.

**Values never appear in summaries.** Progress narration, recaps, and email bodies
reference field *names*, never values. A recap quoting your date of birth can be
forwarded somewhere it doesn't belong.

**Refusals are respected.** "Skip" is a valid answer to any question. The field is
left blank, noted, and not raised again.

## Rollout

1. **Phase 1** — `form-autofill` + `profile-interview`, Word/Excel only, one trusted
   sender. Validates the trigger, profile lookup, interview loop, and reply path.
   ✅ *Passed.*
2. **Phase 2** — add `profile-vault`. By now you know which fields actually recur, so
   the schema is grounded in real forms. *Not yet exercised.*
3. **Phase 3** — PDF. ✅ *Passed — no MCP connector needed.* Cowork fills AcroForms
   in place and preserves the form layer, and correctly refuses to fake a flat scan.

## What Testing Established

Verified against the fixtures in [test-fixtures/](test-fixtures/readme.md):

| Question | Answer |
|---|---|
| Can Cowork fill an AcroForm and preserve it? | **Yes** — fields remained editable after filling; the read-only field was left intact |
| Does it handle a flat, unfillable PDF honestly? | **Yes** — reported it, offered a new document, and asked first |
| Is the directory profile retrievable? | **Yes** — Tier 1 fields filled silently; missing ones degraded to questions rather than guesses |
| Does it tick consent checkboxes unasked? | **No** — it asked which consents to give and ticked only those |
| Does it dedupe fields across a packet? | **Yes** — overlapping concepts asked once, not once per form |

Still unproven:

- **Can Cowork reliably rewrite the vault file it also reads as context?** The vault
  has not been exercised yet — this is the main Phase 2 risk.
- **Do concurrent event-driven runs collide on vault writes?** Untested.

## Reserved Names

Cowork's built-in skills silently override plugin skills with matching names.
Observed built-ins include `PDF`, `Word`, `Excel`, `PowerPoint`, `html`,
`Calendar Management`, `Daily Briefing`, `Meetings`, `Scheduling`, `Communications`,
`Skill Management`, and `goal`.

The list varies by tenant and release — check **Customize → Skills** before naming a
new skill. The three names here are clear of all observed built-ins.

## Author

Troy Taylor — <troy@troystaylor.com> — https://github.com/troystaylor

## Credits

Icons rendered from the **Form** glyph in
[Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)
(MIT). See [NOTICE.md](NOTICE.md).
