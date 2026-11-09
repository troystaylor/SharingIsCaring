---
name: weather-on-the-road
description: Check the rain, snow, ice, fog, wind and sun glare you will meet on a drive, timed to when you get there. Use when asked about weather on a road trip or whether conditions on the way are safe.
---

# Weather on the Road

## When to use this skill

- "What's the weather like on the drive to Portland?", "will I hit rain on the way home?", "is it safe to drive to the pass?"

## How to do it

1. Call `get_weather_along_route` with `origin` and `destination` (text or coordinates). The route and the timing are worked out for you.
2. Set `leaveInMinutes` (0 to 119) when the user isn't leaving right now.
3. Report the worst hazard, where along the route and about when it starts, and any notifications such as rain starting.
4. If the user also wants the weather at the destination, call `get_weather` for it.

## What to watch for

- Azure Maps forecasts only the next 120 minutes of travel, so on a long trip only the first two hours are checked. Say so, and suggest checking again later in the trip.
- In clients that support MCP Apps, the route is colored by hazard with a timeline.
- This is guidance, not a safety guarantee. For severe weather, also check `get_severe_weather_alerts`.
