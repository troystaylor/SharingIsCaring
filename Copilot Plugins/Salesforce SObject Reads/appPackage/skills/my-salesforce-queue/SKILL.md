---
name: my-salesforce-queue
description: Show what needs the current user's attention in Salesforce - open tasks, opportunities closing soon, assigned cases and recently viewed records. Use when asked for my Salesforce to-dos, my day, or what I should work on.
---

# My Salesforce Queue

## When to use this skill

- The user asks what they should work on in Salesforce, or for their open items.
- Cue phrases: "my Salesforce queue", "what's on my plate", "my tasks and deals", "what did I look at recently".

## How to do it

1. Call `getUserInfo` to get the user's Id, name, timezone and local time. Use these for every "my" filter and for interpreting dates.
2. Gather in parallel where possible with `soqlQuery`:
   - Open tasks: `SELECT Id, Subject, ActivityDate, Priority, What.Name FROM Task WHERE OwnerId = '<id>' AND IsClosed = false ORDER BY ActivityDate ASC LIMIT 25`
   - Opportunities closing within 30 days owned by the user.
   - Open cases owned by the user, highest priority first.
3. Call `listRecentSobjectRecords` for `Account` and `Opportunity` to include "what you were just looking at".
4. Present a prioritized list: overdue first, then due today, then this week. Group by type and give one line per item with the record name and why it matters.

## What to watch for

- Use the user's timezone when deciding what is "today" or "overdue".
- Keep the list short (top 10 to 15 items overall) and offer to expand a section.
- This skill is read-only. To complete tasks or update records, use the Salesforce SObject Mutations plugin.
