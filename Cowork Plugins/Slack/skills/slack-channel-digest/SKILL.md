---
name: slack-channel-digest
description: |
  Summarizes recent activity in a Slack channel. Use when the user asks to
  "catch me up on #channel", "summarize Slack today", "what did I miss in
  #eng-platform", "give me a digest of #standup", "what's been happening in
  #incidents", or any request to recap a channel over a recent time window.
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: read
cowork.category: Communication
cowork.icon: Chat
---

# Slack Channel Digest

## What This Skill Does

Pulls recent messages from a Slack channel and produces a structured digest with
the key threads, decisions, action items, and unresolved questions. Designed for
"catch me up" moments — start of day, returning from PTO, or before a meeting.

## When to Activate

- User asks to summarize, recap, or digest a Slack channel
- User asks "what did I miss" or "catch me up" with a channel name (`#foo`) or channel ID
- User asks for highlights from a channel over a time window (today, since yesterday, last 7 days, etc.)

## Workflow

1. **Identify the channel and window.**
   - Channel: accept `#channel-name`, channel ID (`C0123456789`), or "the channel I last asked about".
   - Window: default to the last 24 hours if unspecified. Otherwise parse phrases like "today", "since Friday", "last week" into an explicit start time.

2. **Resolve the channel.** If the user gave a name, call `list_channels` and find the matching channel ID. Confirm with the user only if multiple channels match.

3. **Fetch history.** Call `get_channel_history` with the channel ID and the resolved time window. If the channel is high-volume, page until you have the full window or hit 500 messages — whichever is first.

4. **Cluster and summarize.** Group messages into threads. For each thread, identify:
   - Topic (one short noun phrase)
   - Participants (names, not user IDs — call `get_user_info` if needed)
   - Decisions made
   - Action items (who, what, deadline if stated)
   - Unresolved questions

5. **Render the digest** using the output format below. Lead with the most consequential threads. Include a `permalink` for each thread so the user can jump in.

6. **Offer next steps.**
   - "Want me to recap a specific thread in more detail?" → hand off to `slack-thread-recap`.
   - "Want me to post a summary back to the channel or DM it to someone?" → hand off to `slack-post-update`.

## Tools

- `list_channels` — resolve channel name to ID
- `get_channel_history` — fetch the message window
- `get_user_info` — render user IDs as readable names

## Output Format

Open with a 1-sentence headline of the channel state. Then:

### Top threads

| Topic | Participants | Decisions | Action items | Permalink |
|---|---|---|---|---|
| Outage retro for incident #4821 | @alice, @bob, @carol | Postmortem owner = @alice; deadline 5/22 | @bob to draft timeline by EOD; @carol to schedule review | [link] |
| Q3 roadmap input | @dave, @erin | Ship API v2 before mobile rewrite | @dave to update planning doc | [link] |

### Unresolved questions

- Did anyone confirm the Datadog quota bump? (@erin asked, no answer)
- Are we still blocked on the security review for the new IdP integration?

### Quiet but worth noting

- 2 GitHub deploy notifications (production, both green)
- 1 PagerDuty alert that auto-resolved in 3 minutes

## Notes

- Don't dump raw messages. Cluster and summarize.
- Replace user IDs (`U0123ABCD`) with display names. Fall back to the ID only if the lookup fails.
- If the channel has no activity in the window, say so plainly — don't pad.
