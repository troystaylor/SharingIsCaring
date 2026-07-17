---
name: cross-m365-workflow
description: |
  Chain browser automation, code execution, and Microsoft 365 actions into multi-step
  workflows. Use when the user's request spans web + M365 (e.g., "research this vendor
  and email a summary", "check this site daily and notify me").
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: report-and-summarize
---

# Cross-M365 Workflow

## When to Activate

- "Research [topic] and email the results to [person]"
- "Go to [URL], extract [data], and put it in a spreadsheet on my OneDrive"
- "Check [site] for updates and message me in Teams"
- Any request combining web/code actions with M365 actions

## Workflow

1. **Execute the web/code portion** using Ranger tools (browse, extract, execute_code, create docs)
2. **Save intermediate results** to memory: `save_memory(key, value)`
3. **Perform M365 action** using Work IQ tools:
   - Send email → Work IQ Mail tools
   - Save to OneDrive → Work IQ OneDrive tools
   - Post in Teams → Work IQ Teams tools
   - Schedule meeting → Work IQ Calendar tools
   - Look up person → Work IQ User tools

## Missing Tools

If any Work IQ tool is unavailable, tell the user exactly which to enable:

- **Email**: "This workflow needs Work IQ Mail. Please enable it in Cowork Settings > Tools > Work IQ Mail, then try again."
- **Files**: "Please enable Work IQ OneDrive (personal) or Work IQ SharePoint (team files)."
- **Teams**: "Please enable Work IQ Teams in Cowork Settings > Tools."
- **Calendar**: "Please enable Work IQ Calendar in Cowork Settings > Tools."
- **People**: "Please enable Work IQ User in Cowork Settings > Tools."

Do NOT attempt to work around missing tools. The web/code portion still works — offer to save results to memory for later.

## Deferred Pattern

If Work IQ tools aren't available but the user wants M365 action:
1. Complete the web/code portion
2. Save results to memory: `save_memory(key: "pending/[task]", value: results)`
3. Tell user: "I've completed the research and saved results. Enable [Work IQ server] and ask me to send/save it."
