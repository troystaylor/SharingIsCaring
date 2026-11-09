---
name: track-a-storm
description: Find active tropical storms, hurricanes, cyclones and typhoons and show their tracks and forecasts. Use when asked whether there are storms, where a hurricane is heading, or how strong it is.
---

# Track a Storm

## When to use this skill

- "Are there any hurricanes right now?", "where is Typhoon X going", "what storms were in the Atlantic in 2024".

## How to do it

1. Call `get_tropical_storms` with `scope` `active` for storms happening now. It includes the past track and forecast track of the strongest ones.
2. To look up a past or named storm use `scope` `search` with `year` and, if known, `basinId` (AL Atlantic, EP East Pacific, CP Central Pacific, NP Northwest Pacific, NI North Indian, SI South Indian, SP South Pacific).
3. For one storm's track use `scope` `track` with `year`, `basinId` and `govId`.
4. Report each storm's name, basin, strength, movement and where it is forecast to go. If none are active, say so.
5. For alerts at a place near a storm, also use `get_severe_weather_alerts`.

## What to watch for

- Strength categories are approximate, because agencies measure wind over different periods.
- Forecast tracks have uncertainty that grows with time. For safety decisions, point the user to their national weather agency.
