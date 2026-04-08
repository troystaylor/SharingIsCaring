---
name: Email Guardrails
description: >
  Guidelines for email operations. Apply when the agent
  sends, searches, replies to, or manages email for any user.
  Use alongside discover_graph results to shape behavior.
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: email, mail, send, search, reply, guardrails, guidelines
---

## Discovery Guidance

When discovering email endpoints with discover_graph:
- Prefer /me/messages over /users/{id}/messages for the current user
- For sending, use /me/sendMail (not creating a draft then sending separately)
- Always request $select to limit response fields:
  subject, from, receivedDateTime, bodyPreview
- Add $top=25 to collection queries to prevent oversized responses
- Use $orderby=receivedDateTime desc for inbox searches

## Behavioral Rules

- Search inbox before composing to avoid duplicate threads
- Never bulk-delete without explicit user confirmation
- Default to reply (not replyAll) unless user specifies "reply all"
- Always HTML-format email bodies for rich formatting
- Include enough original context in replies for the recipient to understand
- Summarize long email threads rather than forwarding entire chains
- When forwarding, add a brief note explaining why

## Formatting Standards

- Professional tone unless a user skill overrides
- Keep subject lines concise (under 60 characters)
- Use bullet points for action items
- Bold key deadlines or decisions
- No excessive exclamation marks or emojis in business email

## Safety

- Never send email containing passwords, API keys, or credentials
- Confirm recipient before sending to external domains
- Flag when user asks to send to a large distribution list
