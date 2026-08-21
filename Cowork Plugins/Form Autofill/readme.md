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

## Requirements

Copilot Cowork, and nothing else.

This is a skills-only plugin: no MCP server, no connector, no OAuth, no API keys, no
Azure resources, and nothing to configure after install. That's why `manifest.json`
has no `agentConnectors` block. Testing confirmed a hosted PDF-filling tool isn't
needed either — Cowork fills AcroForms natively.

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

## The Vault (optional)

The vault is **opt-in, and the plugin works fully without it.** If you decline, the
skills just ask you for Tier 2 details each time and nothing else changes — filling,
verification, and the reply all behave identically. The vault exists solely to stop
you retyping the same answers.

If you opt in, saved answers live at **`/Documents/Personal/form-profile.md`** in
your OneDrive — deliberately *outside* the skills folder.

This matters: Cowork lets you share skills with colleagues. A vault stored inside a
skill folder would be shared along with it. Keeping the data separate means sharing
the skill shares the protocol and nothing else.

Apply a **Confidential sensitivity label** to that file once created. Purview
governs Cowork, so the label gives you real DLP enforcement and audit rather than
relying on the skills' own good behavior.

You can read and edit the file directly — it's plain Markdown — and you can ask
Cowork to show, correct, or delete its contents at any time.

## What the Skills Can and Cannot Promise

The skills control exactly one thing: **whether a value is written to the vault**.
They do not control platform-level retention, and they no longer claim to.

Microsoft 365 Copilot personalization and memory *can* retain details inferred from
chat history, stored in your Exchange mailbox, and enhanced personalization is on by
default. Whether anything is actually retained depends on tenant and user settings —
in testing the memory store was queried directly and found **empty**, so treat this
as a capability to be aware of rather than an observed leak.

What follows:

- The skills promise only that a value is **not written to the vault**. They never
  claim it is stored nowhere, because they cannot know that.
- For sensitive fields the skills **recommend leaving the field blank** so you
  complete it by hand. Typing a government identifier into a conversation is a
  disclosure in itself, whatever happens to it afterwards.
- A form returned with a few blanks and a clear note about them is a better outcome
  than one filled at the cost of putting an identifier into a conversation.

Review what is retained under **Copilot → Settings → Personalization and memory**,
where memories can be viewed and cleared. Admins can turn enhanced personalization
off tenant-wide.

## Completed Forms Are Not Data Sources

Testing surfaced a real contamination path worth knowing about.

A filled form left in OneDrive **becomes discoverable to later runs**. In testing,
a completed travel questionnaire was found by name and used as a data source for an
external credit application — output from one task silently became input to another,
with a different audience and no confirmation the values were current.

The skills now prohibit this explicitly:

- An absent vault means **ask**, never improvise. No searching OneDrive for a
  lookalike file, no reading a completed form as a stand-in.
- Values come from exactly three places: the directory profile, the vault, and
  answers given in the current run.

Two practical consequences:

- **Clear out completed forms you don't need**, especially ones holding personal
  details. They are findable.
- **A governed vault is safer than an ad-hoc file.** A single confirmed source with
  provenance dates and an explicit prohibited-field list beats whatever a filename
  search happens to surface.

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

> When I receive an email with "Forms to complete" in the subject, triage any PDF,
> Word, or Excel attachments, fill out the ones that are forms, ask me about anything
> you can't fill, and draft a reply to the sender with the completed files attached.

**Scope the trigger with a subject keyword.** Without a filter it fires on every
email with an attachment, drafting replies to real correspondents.

Event-driven tasks default to draft-and-approve, so the reply waits for you.
Attachments on the triggering message are readable directly — no re-upload needed.

**Keep trigger instructions minimal.** Anything you put in the trigger prompt
overrides skill behavior. In testing, a prompt telling the agent to "use the profile
vault / saved personal details" caused it to search OneDrive and read an unrelated
file when no vault existed. Describe *what* you want done and let the skills decide
*how*.

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
| Does it handle a flat, unfillable PDF honestly? | **Yes** — reported it, offered an alternative, and asked first |
| Is the directory profile retrievable? | **Yes** — Tier 1 fields filled silently; missing ones degraded to questions rather than guesses |
| Does it tick consent checkboxes unasked? | **No** — it asked which consents to give and ticked only those |
| Does it dedupe fields across a packet? | **Yes** — overlapping concepts asked once, not once per form |
| Does the email trigger fire and reach attachments? | **Yes** — attachments read from the triggering message with no re-upload |
| Does the reply wait for approval? | **Yes** — drafted to the original sender, never sent |
| Does the reply body leak values? | **No** — field names only, with every blank itemised |
| Does it flag an external recipient? | **Yes** — warned that the form was leaving the tenant |
| Does it surface conflicting sources? | **Yes** — disclosed that two sources held different addresses and asked which to use |

Still unproven:

- **The source-restriction rules are untested.** They were added after a real
  contamination incident and have not been re-exercised. This is the highest
  priority for the next run.
- **Can Cowork reliably rewrite the vault file it also reads as context?** The vault
  has never been exercised — the main Phase 2 risk.
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
