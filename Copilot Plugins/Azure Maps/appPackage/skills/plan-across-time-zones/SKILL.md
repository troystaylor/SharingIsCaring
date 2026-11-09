---
name: plan-across-time-zones
description: Show the local time in several places and find meeting times that fall in everyone's working hours. Use when asked what time it is somewhere, to schedule across time zones, or about sunrise, sunset and daylight saving changes.
---

# Plan Across Time Zones

## When to use this skill

- "What time is it in Tokyo?", "find a meeting time for Seattle, London and Mumbai", "when do the clocks change in London?"

## How to do it

1. Call `get_timezone` with `locations` (cities or coordinates, up to 12) and, if useful, `timezoneIds` such as `Asia/Kolkata`.
2. Set `workStart` and `workEnd` (HH:mm) if the user's working hours aren't 09:00 to 17:00. Set `timeStamp` to look at another date, including across a daylight saving change.
3. Report each place's local time and UTC offset, and the windows when everyone is in working hours. If there is none, give the hours with the fewest people outside working hours and say so.
4. Mention an upcoming clock change when it affects the meeting date.

## What to watch for

- In clients that support MCP Apps, a world clock and meeting planner shows the overlap.
- For Windows time zone names use `convert_windows_timezone`. To list zone IDs use `list_timezones`.
