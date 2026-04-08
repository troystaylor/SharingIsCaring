---
name: Skill Author
description: >
  How to create, update, and manage skill files. Use when saving
  user preferences, creating org templates, or reviewing existing skills.
  Also use when a user corrects output format a second time or says
  remember this, always do it this way, or save my preferences.
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: skill, author, create, update, format, template, meta
---

## Skill File Format

Every skill MUST use this structure.

### YAML Frontmatter (required fields)

```yaml
---
name: Human-readable skill name
description: >
  When to use this skill. Cowork and Power SkillDrive match on this
  field, so be specific about triggers and avoid overlapping with
  other skills.
author: agent | user UPN | developer
created: 2026-04-08
updated: 2026-04-08
expires: 2026-12-31 | never
scope: org | user
user: troy.taylor@contoso.com  # user-scoped skills only
status: active | draft | retired
tags: comma, separated, keywords, for, search
---
```

### Body Sections (Markdown)

#### For operational skills (behavioral guidance for Graph operations)

1. **Discovery Guidance** — Tips for using discover_graph:
   - Preferred endpoints and patterns
   - Recommended $select, $filter, $orderby values
   - Performance optimization hints

2. **Behavioral Rules** — How to use the discovered operations:
   - Sequencing (e.g. "search before sending")
   - Confirmation requirements (e.g. "never bulk-delete without asking")
   - Default choices (e.g. "reply not replyAll")

3. **Formatting Standards** — Output formatting for the domain

4. **Safety** — What NOT to do

2. **Constraints** — What NOT to do with these operations

#### For behavioral skills (user preferences, org standards)

1. **Context** — When and why this skill applies
2. **Inputs** — What information is needed before execution
3. **Steps** — Numbered instructions the agent follows
4. **Output Format** — Expected structure of the result
5. **Constraints** — What NOT to do

#### For combined skills (operations + behavior)

Include both sections. Operations tell the agent WHAT to call.
Behavior tells the agent HOW to use the results.

## File Naming Convention

- Meta-skill: `_skill-author/SKILL.md`
- Org skills: `org-skills/{task-slug}/SKILL.md`
- User skills: `user-skills/{upn-prefix}/{task-slug}/SKILL.md`

Use lowercase slugs with hyphens. The folder name should describe
the task, not the technology.

Good: `weekly-summary`, `cab-submission`, `vendor-evaluation`
Bad: `mail-api`, `graph-endpoint`, `POST-me-sendMail`

## When to Create a Skill

- User corrects output format or preferences a second time
- User explicitly says "remember this" or "always do it this way"
- A task has org-specific formatting not covered by built-in behavior
- You need to learn a new Work IQ operation not yet covered by existing skills

## When to Update a Skill

- User provides a correction that conflicts with the existing skill
- Read the current skill first, merge the change, overwrite with updated date
- Never discard existing instructions — merge unless directly contradicted

## When NOT to Create a Skill

- One-time requests with no pattern
- Information that changes frequently (use live data instead)
- Anything containing passwords, tokens, API keys, or secrets
- Duplicating an existing skill's scope (merge into the existing one instead)

## After Saving a User Skill

Always offer to share the skill file with the user as read-only:

"I've saved your [task] preferences. Want me to share the file
so you can review what I'll follow?"

Use the save tool with the `shareWith` parameter set to the
user's email address. The connector handles the sharing
automatically via the Graph invite API.

## After Saving an Org Skill

Do not share automatically. Org skills are managed by administrators.
Confirm the save and report the file path.

## Example: Creating a User Skill

User says: "Always put blockers first in my status reports"

1. Scan for existing skill: query "status report {user name}"
2. If found: read it, merge the new instruction, save with updated date
3. If not found: create new skill:

```markdown
---
name: Troy's Status Report
description: Format status reports for Troy Taylor with blockers first
author: agent
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: user
user: troy.taylor@contoso.com
status: active
tags: status, report, weekly, blockers
---

## Context
Troy prefers a specific format for status reports.

## Steps
1. Gather this week's activity from email and Teams
2. Format with Blockers section FIRST (bold heading)
3. Follow with Completed, In Progress, Next Week
4. Tone: direct, no greeting

## Constraints
- Do not add a greeting or sign-off
- Blockers must always be the first section
```

4. Save via the save tool:
   path="user-skills/troy-taylor/status-report/SKILL.md"
   content=(the full SKILL.md text)
   shareWith="troy.taylor@contoso.com"
5. Connector saves to SPE container and shares as read-only
