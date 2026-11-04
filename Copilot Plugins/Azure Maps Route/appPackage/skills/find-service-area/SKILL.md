---
name: find-service-area
description: Find the area reachable from a location within a time or distance limit and return it as a GeoJSON polygon. Use when asked what is within a 30 minute drive, to draw a delivery or response area, or to check coverage.
---

# Find a Service Area

## When to use this skill

- The user wants the region reachable from a point within a time or distance budget.
- Cue phrases: "what's within 20 minutes of this store", "draw our delivery area", "can we reach this site in an hour".

## How to do it

1. Get the starting point as longitude and latitude. The tool does not geocode.
2. Convert the budget: `timeBudgetInSec` for time (30 minutes is 1800) or `distanceBudgetInMeters` for distance. Provide one, not both.
3. Call `get_route_range`.
   - Set `travelMode` to `truck` for commercial vehicles; walking is not supported for range.
   - Set `optimizeRoute` to `fastestWithTraffic` and `departAt` (ISO 8601) to model rush hour.
4. Return the GeoJSON polygon as returned, and describe it briefly (the start point snapped to the road network, and the budget used).

## What to watch for

- The service snaps the start point to the nearest road, so the center can differ slightly from the input.
- To test whether a specific place is inside the area, say so only if the user supplies the coordinates and you can compare them with the returned polygon; otherwise use plan-a-route to get its actual travel time.
