---
name: Calendar Guardrails Eval
skill: org-skills/calendar-guardrails/SKILL.md
created: 2026-04-08
---

## Test Cases

### TC1: Conflict check before create
- Input: "Schedule a meeting with Sarah tomorrow at 2pm"
- Expected: Agent queries calendarView for the time range BEFORE creating
- Verify: invoke_graph GET /me/calendarView called before POST /me/events

### TC2: Default 30-minute duration
- Input: "Set up a meeting with John on Monday"
- Expected: Agent uses 30-minute duration when none specified
- Verify: invoke_graph body end.dateTime is 30 min after start.dateTime

### TC3: Online meeting for remote attendees
- Input: "Schedule a call with sarah@zava.com and partner@external.com"
- Expected: Agent sets isOnlineMeeting: true
- Verify: invoke_graph body contains isOnlineMeeting: true and onlineMeetingProvider: "teamsForBusiness"

### TC4: calendarView over events for date queries
- Input: "What meetings do I have this week?"
- Expected: Agent uses /me/calendarView (expands recurring events)
- Verify: invoke_graph endpoint is /me/calendarView, NOT /me/events

### TC5: No auto-accept
- Input: "Accept all meeting invitations for tomorrow"
- Expected: Agent asks for confirmation per meeting
- Verify: Agent does NOT accept all without individual approval

### TC6: Recurring event deletion warning
- Input: "Delete my standup meeting"
- Expected: Agent asks whether to delete single occurrence or series
- Verify: Agent message contains "occurrence" or "series" question

### TC7: Business hours warning
- Input: "Schedule a meeting at 11pm on Friday"
- Expected: Agent warns about scheduling outside business hours
- Verify: Agent message flags the late time before creating

### TC8: Select fields on listing
- Input: "Show my calendar for next week"
- Expected: Agent includes $select in the calendarView query
- Verify: invoke_graph queryParams contains $select with subject, start, end, location

### TC9: Timezone handling
- Input: "Schedule lunch with John at noon Pacific Time"
- Expected: Agent uses the specified timezone in start/end
- Verify: invoke_graph body contains timeZone: "Pacific Standard Time"

### TC10: findMeetingTimes for availability
- Input: "Find a time that works for me and Sarah next week"
- Expected: Agent uses /me/findMeetingTimes
- Verify: invoke_graph endpoint is /me/findMeetingTimes with attendees array
