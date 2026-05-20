---
name: slack-search-and-cite
description: |
  Searches Slack across channels and DMs and returns matching messages with
  citations. Use when the user asks to "find Slack messages about X", "who
  mentioned Y in Slack", "search Slack for the decision on Z", "find that link
  someone shared", or any cross-channel Slack lookup.
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: read
cowork.category: Communication
cowork.icon: Search
---

# Slack Search and Cite

## What This Skill Does

Translates a natural-language question into a Slack search query, runs it, and
returns ranked results with full citations (channel, author, timestamp,
permalink). Optimized for "find the source" rather than "summarize a topic".

## When to Activate

- User asks to search Slack, find a Slack message, or find a Slack link
- User asks "who said X" or "who shared Y" with the context being Slack
- User asks where a decision, link, or document was discussed in Slack

## Workflow

1. **Extract the search intent.** Identify:
   - Keywords (verbatim phrases get quoted in the query)
   - Modifiers: `from:@user`, `in:#channel`, `before:`, `after:`, `has:link`, `has:pin`
   - Time window if specified (otherwise no time filter)

2. **Build the query.** Use Slack search syntax. Examples:
   - `"breaking change" in:#api-v2 after:2026-05-01`
   - `outage from:@alice has:link`
   - `decision approved in:#leadership`

3. **Run the search.** Call `search_messages` with the built query.

4. **Rank and dedupe.**
   - Prefer messages that started threads over replies, unless the user asked for replies.
   - Collapse near-duplicate messages (e.g., the same link shared 5 times) into one row noting "shared N times".
   - Drop bot notifications (PagerDuty, GitHub, etc.) unless the user explicitly asked about them.

5. **Cite every result.** Each row must include channel, author, timestamp, snippet, and permalink. If the user asks "what did Alice decide", quote Alice's words directly with the citation — don't paraphrase her into something she didn't say.

6. **Offer next steps.**
   - "Want the full thread context for #2?" → hand off to `slack-thread-recap`.
   - "Want me to DM the person who wrote this?" → hand off to `slack-post-update`.

## Tools

- `search_messages` — the core search call
- `get_user_info` — resolve user IDs to names
- `list_channels` — resolve channel name modifiers if needed

## Output Format

Open with the query you ran (so the user can adjust if it's wrong). Then:

### Results (N matches)

> Query: `"breaking change" in:#api-v2 after:2026-05-01`

| # | Channel | Author | When | Snippet | Permalink |
|---|---|---|---|---|---|
| 1 | #api-v2 | @alice | 2026-05-12 09:14 | "Confirmed breaking change in v2.3 — we'll need to bump the major" | [link] |
| 2 | #api-v2 | @bob | 2026-05-12 09:18 | "+1, I'll update the changelog" | [link] |

If the search returns nothing, suggest 2–3 alternative queries (drop a modifier, broaden the time window, try synonyms) before giving up.

## Notes

- Always quote the verbatim snippet. Don't paraphrase in the table.
- If the user asks "who said X", lead with the most likely single author — but still show at least 3 results so they can verify.
- Slack search is fuzzy. If results look unrelated, say so and offer a tighter query.
