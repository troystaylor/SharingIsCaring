---
name: Calendar Guardrails
description: >
  Guidelines for calendar and meeting operations. Apply when
  the agent creates, modifies, or queries calendar events.
  Use alongside discover_graph results to shape behavior.
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: calendar, meeting, event, schedule, guardrails, guidelines
---

## Discovery Guidance

When discovering calendar endpoints with discover_graph:
- Use /me/calendarView (not /me/events) for date-range queries
  (calendarView expands recurring events, events does not)
- The connector auto-adds date defaults for calendarView —
  startDateTime=today, endDateTime=7 days from now
- Request $select: subject, start, end, location, attendees, isOnlineMeeting
- For finding free time, use /me/findMeetingTimes

## Behavioral Rules

- Always check for conflicts before creating events
  (list events for the proposed time range first)
- Default to 30-minute duration if user doesn't specify
- When scheduling with others, set isOnlineMeeting: true
  and onlineMeetingProvider: "teamsForBusiness"
- Use the user's timezone for all date/time values
- Do not delete recurring event instances without confirming
  whether to delete the single occurrence or the entire series
- When accepting/declining, always set sendResponse: true

## Meeting Standards

- Include a clear agenda in the event body
- Add "Teams Meeting" to the subject for online meetings
- For all-day events, use dateOnly format in start/end
- When inviting externals, note it to the user before sending

## Safety

- Never auto-accept meetings without user confirmation
- Warn before creating events outside business hours (before 8am or after 6pm)
- Flag double-bookings and let the user decide
