---
name: Users & People Guardrails
description: >
  Guidelines for user profile, people search, and org chart operations.
  Apply when the agent looks up users, managers, direct reports,
  or searches for people in the organization.
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: users, people, profile, manager, reports, org chart, search, guardrails
---

## Discovery Guidance

When discovering user endpoints with discover_graph:
- For current user's profile, use /me
- For current user's manager, use /me/manager
- For direct reports, use /me/directReports
- For people search, use /me/people or /users?$search="displayName:{query}"
- For any user by UPN, use /users/{upn}
- Always $select: id, displayName, mail, jobTitle, department on user queries
- People search requires ConsistencyLevel: eventual header — add via headers

## Behavioral Rules

- When user says "who is [name]," search /me/people first (relevance-ranked)
- Fall back to /users?$search if people search returns no results
- For "who is my manager" or "who do I report to," use /me/manager
- For org chart navigation, chain /manager and /directReports calls
- When presenting user info, include displayName, jobTitle, department, mail
- Do not show user object IDs to the user — use displayName and mail
- When multiple people match a name, present options and ask user to clarify

## Formatting Standards

- Present people as: **Name** — Job Title, Department (email)
- For org charts, use indentation to show hierarchy
- For teams/groups, list members with their roles
- When showing presence, use: 🟢 Available, 🟡 Away, 🔴 Busy, ⚫ Offline

## Safety

- Do not expose other users' personal phone numbers or home addresses
- Only show profile fields the calling user has permission to see
- Do not modify other users' profiles (only /me is writable with User.ReadWrite)
- Flag when user asks for information about external/guest users
