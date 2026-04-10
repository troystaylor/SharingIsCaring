# Copilot Cowork Skills

MCP-enabled custom connector for managing Copilot Cowork custom skills stored in your OneDrive.

## Overview

[Copilot Cowork](https://learn.microsoft.com/microsoft-365/copilot/cowork/) is a work delegation engine in Microsoft 365 Copilot that carries out tasks on your behalf — sending emails, scheduling meetings, creating documents, and more. Cowork uses specialized **skills** to accomplish these tasks.

Beyond the 13 built-in skills, Cowork supports up to **20 custom skills** stored as `SKILL.md` files in your OneDrive `/Documents/Cowork/Skills/` folder. This connector provides MCP tools to manage those custom skills programmatically.

> **Note:** Copilot Cowork is currently available through the [Frontier preview program](https://adoption.microsoft.com/en-us/copilot/frontier-program/).

## Prerequisites

- Microsoft 365 Copilot license with Frontier preview access
- OneDrive for Business
- An Azure AD app registration with `Files.ReadWrite` delegated permission

## MCP Tools

| Tool | Description |
|------|-------------|
| `list_skills` | List all custom skills in the Cowork Skills folder |
| `get_skill` | Read the SKILL.md content of a specific skill |
| `create_skill` | Create a new custom skill (folder + SKILL.md) |
| `update_skill` | Update an existing skill's SKILL.md file |
| `delete_skill` | Delete a skill folder |
| `validate_skill` | Validate SKILL.md content without saving |

## SKILL.md Format

Each custom skill is a `SKILL.md` file with YAML frontmatter and Markdown instructions:

```yaml
---
name: Weekly Report
description: Generates a weekly status report from my recent emails and calendar.
---

Gather my sent emails and calendar events from the past week, then create
a summary document organized by project.
```

### Requirements

- **name** (required): Display name shown in Cowork
- **description** (required): One-line description used by Cowork to decide when to load the skill
- **Instructions** (required): Markdown content after the frontmatter that tells Cowork how to execute the skill
- Maximum file size: 1 MB
- Maximum custom skills: 20

## Setup

1. Deploy the connector to your Power Platform environment
2. Create a connection using your Microsoft 365 account
3. Grant `Files.ReadWrite` permission when prompted
4. Add the connector to a Copilot Studio agent or use in Power Automate

## Example Skills

### Daily Standup Summary

```yaml
---
name: Daily Standup Summary
description: Prepares a daily standup summary from yesterday's work items and today's calendar.
---

1. Check my calendar for meetings I attended yesterday and extract key action items.
2. Review my sent emails from yesterday for completed tasks or decisions made.
3. Look at today's calendar for upcoming meetings and commitments.
4. Create a brief standup summary with three sections:
   - What I accomplished yesterday
   - What I'm working on today
   - Any blockers or concerns
```

### Client Follow-Up Drafter

```yaml
---
name: Client Follow-Up Drafter
description: Drafts follow-up emails after client meetings using meeting notes and transcripts.
---

After I mention a client meeting:
1. Find the most recent meeting with that client from my calendar.
2. If a transcript exists, summarize the key discussion points and commitments.
3. Draft a professional follow-up email that includes:
   - Thank them for the meeting
   - Recap the key decisions and action items
   - Suggest next steps with proposed dates
4. Show me the draft for review before sending.
```

### Expense Report Prep

```yaml
---
name: Expense Report Prep
description: Gathers travel and expense information from emails and calendar to prepare an expense report.
---

When asked to prepare an expense report for a specific time period:
1. Search my email for receipts, booking confirmations, and expense-related messages.
2. Check my calendar for business travel events during that period.
3. Create an Excel spreadsheet with columns: Date, Vendor, Category, Amount, Description.
4. Categorize expenses as: Travel, Meals, Accommodation, Transportation, or Other.
5. Include a summary total at the bottom.
```

## Authentication

This connector uses OAuth 2.0 with Microsoft Graph delegated permissions:

- **Scope**: `Files.ReadWrite`
- **Identity Provider**: Azure AD (certified)
- **Resource**: `https://graph.microsoft.com`

## API Reference

The connector wraps Microsoft Graph OneDrive API endpoints:

- `GET /me/drive/root:/Documents/Cowork/Skills:/children` — List skill folders
- `GET /me/drive/root:/Documents/Cowork/Skills/{name}/SKILL.md:/content` — Read skill
- `PUT /me/drive/root:/Documents/Cowork/Skills/{name}/SKILL.md:/content` — Create/update skill
- `DELETE /me/drive/root:/Documents/Cowork/Skills/{name}` — Delete skill folder

## Learn More

- [Copilot Cowork overview](https://learn.microsoft.com/microsoft-365/copilot/cowork/)
- [Use Copilot Cowork — Custom skills](https://learn.microsoft.com/microsoft-365/copilot/cowork/use-cowork#create-custom-skills)
- [Copilot Cowork FAQ](https://learn.microsoft.com/microsoft-365/copilot/cowork/cowork-faq)
- [Working with files in Microsoft Graph](https://learn.microsoft.com/graph/api/resources/onedrive)
