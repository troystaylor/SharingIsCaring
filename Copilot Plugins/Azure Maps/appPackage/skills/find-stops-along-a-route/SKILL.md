---
name: find-stops-along-a-route
description: Find gas stations, charging points, restaurants or other places along a route, ranked by detour time. Use when asked for a stop on the way, a charger on a road trip, or coffee between two cities.
---

# Find Stops Along a Route

## When to use this skill

- "Find a charger on the way to Portland", "where can I get coffee between here and the airport".

## How to do it

1. Call `search_along_route` with `query`, `origin` and `destination` (text or coordinates). The route is calculated for you.
2. Set `maxDetourTimeSeconds` (default 600, up to 3600) and `limit`.
3. For charging, set `connectorSet` (for example `IEC62196Type2CCS` or `Chademo`). Use `brandSet` or `categorySet` to narrow results (look up category IDs with `get_poi_categories`).
4. List the places with the detour time of each and where they are along the route.

## What to watch for

- A negative detour means the place is on a faster path than the original route.
- If nothing is found, widen the detour or change the query.
- For the weather on that drive, use the weather-on-the-road skill.
