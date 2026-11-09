---
name: best-time-to-leave
description: Find the best time to leave or arrive for a trip by comparing predicted traffic across a day. Use when asked when to leave, how to avoid rush hour, or how long a commute takes at different times.
---

# Best Time to Leave

## When to use this skill

- "When should I leave to avoid traffic?", "how bad is the commute at 8 versus 9?", "what time should I leave to arrive by 9?"

## How to do it

1. Call `compare_departure_times` with `origin` and `destination` (text or coordinates).
2. Set `date` (YYYY-MM-DD, default tomorrow), `startTime` and `endTime` (HH:mm local), and `stepMinutes` (15 to 240; at most 24 tests are made). A morning commute is usually 06:00 to 10:00 at 30 minutes.
3. Set `timeMode` to `arriveAt` when the user has a deadline, otherwise `departAt`.
4. Report the best and worst times, how many minutes the right timing saves, and that times are local to the origin.

## What to watch for

- These are predictions from historical and live traffic, not guarantees.
- If the difference is only a few minutes, say traffic barely changes the trip.
- In clients that support MCP Apps, a chart shows travel time across the day.
