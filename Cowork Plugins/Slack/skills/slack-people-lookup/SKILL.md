---
name: slack-people-lookup
description: |
  Finds people in Slack by name, handle, email, or role description, and
  returns their profile and availability. Use when the user asks "who is
  @jane", "who runs #incidents", "find the on-call for payments", "who's the
  Slack admin", or "what's @bob's email and timezone".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: read
cowork.category: Communication
cowork.icon: Person
---

# Slack People Lookup

## What This Skill Does

Resolves a person reference (handle, name, email, or descriptive phrase) to a
Slack user, returning their display name, title, email, timezone, status, and
DM link.

## When to Activate

- User asks "who is @handle" or "who is <full name>"
- User asks for someone's role, title, email, or timezone in Slack
- User asks to find the right person for a topic (e.g., "who owns billing")
- User wants a DM link or wants to start a conversation with someone

## Workflow

1. **Classify the reference.**
   - Starts with `@` and no spaces → Slack handle, look up directly.
   - Looks like an email → email lookup.
   - Free-form name → fuzzy match against `list_users`.
   - Descriptive ("on-call for payments", "Slack admin") → search instead via `slack-search-and-cite` or ask the user to be more specific.

2. **Resolve the user.**
   - Handle: `get_user_info` with the handle (strip the leading `@`).
   - Email: `launch_slack` with `endpoint: users.lookupByEmail`.
   - Name: `list_users`, then filter client-side by `real_name` and `display_name`. If multiple matches, present the candidates and ask the user to pick.

3. **Fetch profile and presence.** From the resolved user object, pull:
   - Display name, real name, title
   - Email (if visible to caller)
   - Timezone (label + current local time)
   - Status text and emoji
   - Whether the account is a bot, deleted, or restricted

4. **Render the profile** using the format below.

5. **Offer next steps.**
   - "Want me to DM them?" → hand off to `slack-post-update` with the user's DM channel.
   - "Want recent messages from them?" → hand off to `slack-search-and-cite` with `from:@handle`.

## Tools

- `get_user_info` — handle lookup
- `list_users` — name fuzzy match
- `launch_slack` (calling `users.lookupByEmail`) — email lookup

## Output Format

```
**@jane.doe** — Jane Doe
- Title: Senior Software Engineer
- Email: jane.doe@zava.com
- Timezone: America/Los_Angeles (currently 14:22 local)
- Status: 🏖️ Out until Monday
- Account: active user
```

If multiple matches:

> Found 3 people matching "jane":
>
> | Handle | Name | Title |
> |---|---|---|
> | @jane.doe | Jane Doe | Senior Software Engineer |
> | @jane.smith | Jane Smith | Product Manager |
> | @janed | Jane Davis | Designer |
>
> Which one did you mean?

## Notes

- Never invent profile fields. If email isn't visible to the caller, say "not visible to me" rather than guessing.
- Respect deleted/restricted accounts — still return the profile but flag the state.
- For descriptive lookups, prefer asking the user to clarify over guessing the wrong person.
