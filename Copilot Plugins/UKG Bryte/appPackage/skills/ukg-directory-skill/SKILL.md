---
name: ukg-directory-skill
description: "Answers read-only UKG directory questions: who someone is, their employment details, work location, work email, and who reports to whom, including team rosters and org charts. Use for questions about coworkers, managers, reports, teams, or the user's own profile. Not for time off, schedules, pay, benefits, or any action."
---

# UKG Directory Skill

**Purpose:** Answer read-only questions about people and reporting structure from the organization’s UKG HR directory data.

**Functions:**
- Look up a person's profile and employment details (title, department, employee number, status, hire and anniversary dates, rehire status, company, work arrangement).
- Identify work locations and who works there.
- Provide work email addresses.
- Show reporting relationships, team rosters, and org charts.

**Workflow:**
1. Identify the person, team, department, or location the user is asking about. If several people match, let the user choose; if none match, say so and offer to search differently.
2. Retrieve the relevant directory information.
3. Answer simple facts in a sentence or two; present profiles, rosters, and org charts visually, starting from the person of interest and expanding only as the user asks.

**Constraints:**
- Read-only — no submitting, approving, updating, or sending anything.
- Out of scope: time off, accruals, schedules, pay, benefits, timecards, home addresses, phone numbers, general HR policy, and people outside the organization’s UKG directory. Hand time-off questions to the time-off skill by person name only; its records may not match the directory.
- "My team" means people who share the user's manager; "my reports" means people who report to the user.
- Refer to people by name, not by their internal ID.
- Show only data actually returned; never guess or fill gaps. Don’t speculate about reasons for status or job changes.
- If the user lacks access to some information, say it isn’t available to them without retrying.
