---
name: find-service-area
description: Show the area reachable from a place within a time or distance, and find places inside it. Use when asked what is within 15 minutes, to draw a delivery or coverage area, or how far an electric vehicle can go.
---

# Find a Service Area

## When to use this skill

- "What's within a 20 minute drive of our store?", "which pharmacies can I reach in 10 minutes", "how far can I go on 40 kWh".

## How to do it

1. Call `get_route_range` with `origin` (text or coordinates) and `timeBudgetsMinutes`. Give several, such as `[10, 20, 30]`, to show nested areas. Use `distanceBudgetInMeters` for distance.
2. To list places inside the area, set `placesQuery` (for example "pharmacy", "grocery", "EV charging").
3. Set `travelMode` to `driving` or `truck`, and `departAt` to use traffic for a given time.
4. For the same budget from many origins (the coverage of every store), use `get_route_range_batch`.
5. For electric or combustion range from a battery or fuel level, or for a bus, van or motorcycle, use `get_reachable_range_advanced` with the budget (`energyBudgetInkWh`, `fuelBudgetInLiters`) and the consumption model the user gives.
6. Report the area of each band and the places found.

## What to watch for

- Walking isn't supported for range.
- In clients that support MCP Apps, the areas appear on a map where the user can change the time and move the start.
- Areas are estimates based on the road network.
