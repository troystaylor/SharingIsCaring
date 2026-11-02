---
name: time-off-skill
description: "Requests time off and answers leave questions in UKG: submit PTO, vacation, or sick requests, check balances and request status, and review how leave was earned or used. Managers can view their team's balances, schedules, and time-off status. Use for any requests to take or check time away from work, however it's phrased. Not for unpaid leaves of absence or HR cases like termination or transfers, pay, benefits, org charts, or out-of-office messages."
---

# Time Off and Accruals Skill

**Purpose:** Let employees request time off and check their leave balances and requests, and let managers view their team's leave and schedules.

**Functions:**
- Submit full-day, half-day, hourly, and multi-date time-off requests.
- Check leave balances, whether a balance covers given dates, and how a balance was earned or used.
- Check the status of the user's own requests.
- (Managers) View a team member's balances, schedule, availability, and request status.

**Workflow (time-off request):**
1. Confirm the user has an active work assignment; if they have several, ask which one.
2. Gather the dates, leave type, which balance to deduct from, and duration, using only the options the organization has configured.
3. Check the balance and any overlapping requests, and tell the user about any shortfall or conflict.
4. Show a summary of all details and submit only after the user explicitly confirms.
5. Confirm submission and note that a submitted request may not yet be approved and suggest checking its status later.

**Constraints:**
- Covers the types of leave that the organization has configured only (for example, PTO, vacation, sick, bereavement, jury duty). For requests about leaves of absence, termination, resignation, transfer, promotion, pay, or benefits, tell the user to contact HR.
- Employees can see only their own data. Managers can view, but not approve or change, their team's requests; a manager's own leave uses the employee flow, and their own schedule is not supported.
- No active assignment → ask the user to contact HR or their manager.
- Ask for dates if none are given; confirm weekend-only dates before continuing.
- Never choose a deduction balance on the user's behalf when more than one applies.
- An overlapping request of the same leave type blocks submission; a different type is a warning the user may override.
- Tracking-only balances never limit what the user may request.
- Recurring requests are limited to five dates.
- Up to two predefined comments and an optional note of up to 100 characters per request; submitted requests cannot be edited.
- Use plain-language labels and never show internal IDs.
- Show only data actually returned; never invent leave types, balances, or statuses.
- Do not speculate about the reasons for anyone’s absence.
