---
name: slack-thread-recap
description: |
  Recaps a specific Slack thread from a message link or channel+timestamp.
  Use when the user pastes a Slack permalink, asks to "summarize this thread",
  "what's the TL;DR of this thread", "what was decided in this thread", or
  "catch me up on this conversation".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: read
cowork.category: Communication
cowork.icon: CommentMultiple
---

# Slack Thread Recap

## What This Skill Does

Pulls the full reply chain of a single Slack thread and produces a structured
TL;DR with positions, decisions, action items, and open questions.

## When to Activate

- User pastes a Slack permalink (URL containing `/archives/<channel>/p<timestamp>`)
- User asks to summarize, TL;DR, or recap a specific thread
- User asks "what was decided in this thread" or "what's the conclusion"

## Workflow

1. **Parse the input.** Accept any of:
   - Full Slack permalink — extract channel ID and thread timestamp (`ts`) from the URL.
   - Explicit `channel` + `thread_ts` arguments.
   - Phrase "the last thread" → use the most recent thread from the current conversation context.

2. **Fetch the thread.** Use `launch_slack` with `endpoint: conversations.replies` and `body: { "channel": "<id>", "ts": "<thread_ts>" }`. Page through `response_metadata.next_cursor` until all replies are loaded.

3. **Resolve participants.** Map every user ID to a display name with `get_user_info`. Cache lookups so each user is fetched once.

4. **Analyze the thread.** Identify:
   - Original question or proposal (the parent message)
   - Each participant's position in 1 sentence
   - Decisions reached (explicit "let's go with X" / "approved" / "merged")
   - Action items with owner and deadline
   - Open questions or disagreements left unresolved

5. **Render the recap** using the output format below.

6. **Offer next steps.**
   - "Want me to post a summary as a thread reply?" → hand off to `slack-post-update` with `thread_ts` preserved.
   - "Want to find related threads?" → hand off to `slack-search-and-cite`.

## Tools

- `launch_slack` (calling `conversations.replies`) — fetch the thread
- `get_user_info` — resolve IDs to names

## Output Format

```
**Thread:** #channel-name — started by @author on YYYY-MM-DD HH:MM
**Original message:** "<one-sentence quote or paraphrase>"
**Replies:** N from M participants

### Positions
- **@alice** — wants to roll forward and patch the regression
- **@bob** — wants to roll back to v2.2 first, then re-attempt
- **@carol** — neutral, asked about customer impact

### Decisions
- Roll back to v2.2 (decided 2026-05-12 11:42, called by @alice)
- Re-attempt deploy with the hotfix on 2026-05-13

### Action items
| Owner | Action | Due |
|---|---|---|
| @bob | File the rollback CR | EOD 5/12 |
| @carol | Email impacted customers | Before re-deploy |

### Open questions
- Do we need a postmortem given the customer impact?
```

## Notes

- If a permalink is malformed, ask the user to re-paste rather than guessing.
- If `conversations.replies` returns only the parent and no replies, say so plainly — there's nothing to recap.
- Don't invent decisions. If the thread didn't reach one, say "no decision reached".
