---
name: slack-remind-me
description: |
  Creates, lists, completes, or deletes Slack reminders for the user (or for
  someone else), with explicit confirmation. Use when the user asks to
  "remind me about this in 2 hours", "set a Slack reminder at 4pm to follow
  up with Bob", "remind Alice to review the PR tomorrow morning", "what
  Slack reminders do I have", or "delete that reminder".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: write
cowork.category: Communication
cowork.icon: Bell
---

# Slack Remind Me

## What This Skill Does

Wraps Slack's reminders API:

- **Add** — `reminders.add` creates a one-shot reminder.
- **List** — `reminders.list` shows the user's reminders.
- **Complete** — `reminders.complete` marks one done.
- **Delete** — `reminders.delete` removes one.

All write paths go through `launch_slack` or `complete_or_delete_reminder` (`destructiveHint: true`) and
**always render the reminder text + target user + resolved fire time before
calling the API**.

## When to Activate

- User asks to "remind me", "remind <person>", "set a Slack reminder"
- User asks to follow up on something at a specific time
- User asks "what reminders do I have?" or to clean up reminders

## Workflow — Add

> **Confirm before write.** Show the reminder text, who it's for, and when it will fire.

1. **Parse the reminder text.** Use what the user said verbatim. If they referenced "this" (a message in context), include a permalink in the text so they have something to click.

2. **Parse the fire time.**
   - "in 2 hours" → now + 7200 → Unix ts.
   - "at 4pm" → today's 16:00 local → Unix ts (if past, suggest tomorrow's 16:00).
   - "tomorrow morning" → tomorrow 09:00 local → Unix ts.
   - "next Monday at 10am" → resolve to that Monday's 10:00 local → Unix ts.
   - "in 30 minutes" → now + 1800 → Unix ts.
   - Slack also accepts natural-language strings like `"in 30 minutes"` for the `time` parameter directly — but resolving to a Unix ts in your code gives the user a clearer preview. Prefer the Unix ts path.

3. **Parse the target user.**
   - Default → the authenticated user (omit `user` argument).
   - "remind Alice to …" → resolve via `slack-people-lookup` and pass `user: <U-id>`.
   - Reminders for someone else only work if your token has scope; Slack will reject otherwise.

4. **Render the preview.** Show:
   - Who: "yourself" or the resolved target user
   - When: human-readable time in the user's timezone + relative ("in 2h 14m")
   - What: the exact reminder text Slack will store

5. **Wait for confirmation.** Acceptable: "yes", "set it", "go".

6. **Send.** Call `launch_slack`:
   - `endpoint`: `reminders.add`
   - `arguments`:
     ```json
     {
       "text": "<reminder text>",
       "time": <unix-seconds>,
       "user": "<U-id-or-omit>"
     }
     ```

7. **Confirm.** Echo back, include the reminder id from the response (for later delete/complete).

## Workflow — List

1. Call `launch_slack` with `endpoint: reminders.list`.
2. Render: id, target, fire time (local), text, recurring? (yes/no), completed? (yes/no).
3. No confirmation needed (read-only).

## Workflow — Complete / Delete

1. Identify the target reminder by id (from the list output or the user's description — match on text or fire time, then confirm).
2. Show the reminder about to be modified.
3. Wait for confirmation.
4. Call `complete_or_delete_reminder`:
  - Complete: `{ "reminder": "<id>", "action": "complete" }`
  - Delete: `{ "reminder": "<id>", "action": "delete" }`
5. Confirm.

## Tools

- `slack-people-lookup` — resolve target user (skill hand-off)
- `launch_slack` with `reminders.add` / `reminders.list`
- `complete_or_delete_reminder` for `reminders.complete` / `reminders.delete`

## Confirmation prompt template (add)

```
About to set a Slack reminder:

  For:    yourself
  When:   today at 4:00 PM PT (in 2h 14m)
  Text:   Follow up with Bob on the API review — <permalink>

Set reminder? (yes / no / different time / different text)
```

## Output Format

```
Reminder set for today at 4:00 PM PT. id: Rm12345678.
Use "delete reminder Rm12345678" to cancel.
```

## Notes

- Reminders are **one-shot** via the API. Recurring reminders are not creatable through `reminders.add`; only Slack's `/remind` slash command (or built-in UI) supports them. If the user asks for recurring, explain and offer to set the next instance only.
- "Remind me to do X" → "X" is the literal `text`. Don't rephrase or add boilerplate.
- For reminders that reference a message ("remind me about this"), include the permalink in the text so the reminder is actionable.
- Setting a reminder for another user requires `reminders:write` scope — Slack will reject if the token can't.
- Don't auto-create reminders as a side effect of another skill — every reminder must be user-initiated and explicitly confirmed.
