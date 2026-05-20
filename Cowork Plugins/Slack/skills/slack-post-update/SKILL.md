---
name: slack-post-update
description: |
  Posts a message to a Slack channel, DM, or thread, with explicit user
  confirmation before sending. Use when the user asks to "post to #standup",
  "DM @alice", "share this summary in Slack", "reply in that thread", or
  "send this update to the team".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: write
cowork.category: Communication
cowork.icon: Send
---

# Slack Post Update

## What This Skill Does

Drafts and posts a message to Slack — channel, DM, or thread reply. If the
user's request already specifies both the destination (channel, DM target, or
thread) AND the exact message content, call `send_message` directly. If either
the destination or content is ambiguous or missing, draft a preview and ask
for confirmation first.

## When to Activate

- User asks to post, send, share, DM, or reply in Slack
- User asks to "ship this to #channel" or "send this update to @person"
- User asks to follow up on a previous skill's output by posting it back into Slack

## Workflow

> **Fast path:** if the user's request already names a channel/DM/thread AND gives the exact text (e.g., *"Post 'OAuth shim live' to #all-woodchuck"*), resolve the destination and call `send_message` immediately. No preview needed.
>
> **Confirm path:** if destination or content is ambiguous, draft a preview and wait for explicit user approval ("yes", "send it", "post").

1. **Identify the destination.**
   - Channel: accept `#channel-name`, channel ID, or "the channel we were just in".
   - DM: accept `@handle`, email, or name. Resolve via `slack-people-lookup` if ambiguous.
   - Thread reply: if the user said "reply in that thread" or provided a permalink, capture the `thread_ts`.

2. **Resolve to a channel ID.** Use `list_channels` for channel names. For DMs, use `launch_slack` with `endpoint: conversations.open` to get the IM channel ID. For threads, parse the permalink for `channel` and `thread_ts`.

3. **Draft the message.** Compose Slack-flavored markdown:
   - Bold with `*asterisks*` (Slack mrkdwn, not standard markdown)
   - Inline code with backticks, code blocks with triple backticks
   - User mentions: `<@U0123ABCD>` (resolve from handle if needed)
   - Channel mentions: `<#C0123ABCD>`
   - Links: `<https://example.com|link text>`

4. **Render the preview (confirm path only).** When the request is ambiguous or partial, show the user:
   - Destination (channel name + ID, or DM target name)
   - Whether this is a top-level message or a thread reply (with parent timestamp)
   - The exact text that will be sent, formatted as Slack would render it
   - Any attachments (none by default — uploads go through `slack-bulk-broadcast` or `upload_file`)

5. **Wait for explicit confirmation (confirm path only).** Acceptable: "yes", "send", "post it", "ship it", "go". Anything else → revise.

6. **Send.** Call `send_message` with:
   - `channel` (the resolved ID)
   - `text` (the drafted text)
   - `thread_ts` (only if replying in a thread)
   - `unfurl_links: true` unless the user requested otherwise

7. **Confirm and link.** After the call returns, report the result with a permalink so the user can verify.

## Tools

- `list_channels` — resolve channel name to ID
- `slack-people-lookup` — resolve DM target (skill hand-off)
- `launch_slack` (calling `conversations.open`) — open or get an IM channel
- `send_message` — the write call

## Confirmation prompt template

```
About to post to **#standup** as a top-level message:

> *Daily update — 2026-05-18*
> - Shipped the API v2.3 rollback
> - Postmortem draft is in the doc, review by EOD
> - No P1s open
>
> /cc <@U0ALICE>

Send it? (yes / no / revise)
```

## Output Format (after send)

```
Posted to #standup at 2026-05-18 09:32. Permalink: <link>
```

## Notes

- Fast path is the default when the user's request is unambiguous. Don't invent a preview round-trip if both destination and exact text are already specified.
- If posting to a public channel the user isn't a member of, Slack will reject; offer to join the channel first or to use a different target.
- If the user revises on the confirm path, re-render and re-confirm. Don't send a stale draft.
- Don't add signatures, sign-offs, or boilerplate the user didn't ask for.
