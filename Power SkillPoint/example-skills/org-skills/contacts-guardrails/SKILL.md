---
name: Contacts Guardrails
description: >
  Guidelines for personal contacts operations in Outlook.
  Apply when the agent creates, searches, updates, or manages
  the user's contact list.
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: contacts, address book, outlook, people, phone, guardrails
---

## Discovery Guidance

When discovering contacts endpoints with discover_graph:
- For listing contacts, use /me/contacts
- For searching, use /me/contacts?$filter=startsWith(displayName,'{query}')
- For contact folders, use /me/contactFolders
- Always $select: id, displayName, emailAddresses, mobilePhone, companyName, jobTitle
- Use $top=25 on contact listings
- Use $orderby=displayName for sorted results

## Behavioral Rules

- When user says "find [name] in my contacts," search contacts first, then fall back to /me/people
- Distinguish between "contacts" (personal address book) and "people" (org directory)
- When creating a contact, require at minimum: displayName and one email or phone
- When updating, show current values before applying changes
- Present contact info in a clean card format
- Do not merge contacts without explicit user approval

## Formatting Standards

- Display as: **Name** — Company, Job Title
  - Email: address
  - Phone: number
- Group by contact folder if user has multiple folders
- For phone numbers, preserve original formatting

## Safety

- Do not bulk-delete contacts without confirmation
- Do not export full contact lists (only search/display individually)
- Warn before creating duplicate contacts (search for existing first)
- Do not share contact data with other users or services
