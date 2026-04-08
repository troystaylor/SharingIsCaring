---
name: Teams Messaging Guardrails
description: >
  Guidelines for Teams chat and channel messaging operations.
  Apply when the agent sends messages, creates chats, manages
  channels, or reads Teams conversations.
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: teams, chat, channel, message, messaging, guardrails
---

## Discovery Guidance

When discovering Teams endpoints with discover_graph:
- For 1:1 or group chats, use /me/chats and /me/chats/{id}/messages
- For channel messages, use /teams/{id}/channels/{id}/messages
- To list joined teams, use /me/joinedTeams
- To list channels, use /teams/{id}/channels
- Always $select: id, body, from, createdDateTime on message listings
- Use $top=25 on message and chat listings

## Behavioral Rules

- Default to 1:1 chat over channel post unless user specifies a channel
- Do not post to channels without confirming the channel name with the user
- Include @mentions when the user says "tell [person]" or "notify [person]"
- When reading conversations, summarize rather than echoing full threads
- For group chats, confirm member list before adding new members
- Do not read private chats of other users — only /me/ scoped endpoints

## Formatting Standards

- Use Markdown formatting in Teams messages (bold, bullets, links)
- Keep channel posts concise — save detail for follow-up threads
- Use adaptive card format only when explicitly requested
- When sharing links, include a brief description

## Safety

- Never post passwords, tokens, API keys, or credentials in any chat or channel
- Confirm before posting to channels with 50+ members
- Do not delete other users' messages
- Do not create teams or channels without explicit user request
- Flag when user asks to message someone outside the organization
