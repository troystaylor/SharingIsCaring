---
name: check-the-weather
description: Get the weather for a place, including current conditions, rain in the next two hours, hourly and daily forecasts, alerts, air quality and history. Use when asked about weather, whether it will rain, air quality, or conditions for an activity.
---

# Check the Weather

## When to use this skill

- "What's the weather in Seattle?", "will it rain in the next hour?", "is the air quality OK for a run?", "what's normal for this week in October?"

## How to do it

1. For a general question, call `get_weather` with `location` (address, place name or coordinates). It returns current conditions, the next hours and days, the next two hours of rain, alerts and air quality in one call.
2. Set `unit` to `imperial` for Fahrenheit and miles, otherwise `metric`.
3. For one specific thing, use the matching tool: `get_current_conditions`, `get_hourly_forecast`, `get_daily_forecast` (up to 45 days), `get_quarter_day_forecast`, `get_minute_forecast`, `get_severe_weather_alerts`, `get_current_air_quality`, `get_air_quality_hourly`, `get_air_quality_daily`.
4. For "is it good for running, golf, hiking, flying" use `get_daily_indices`. For past weather, normals or records use `get_historical_weather` with `startDate` and `endDate`.
5. Lead with the answer, then the detail. Mention any active alert first.

## What to watch for

- Forecasts beyond a few days are less certain.
- In clients that support MCP Apps, `get_weather` shows a dashboard.
- Never answer a weather question from memory; always call a tool.
