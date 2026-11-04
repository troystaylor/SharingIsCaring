---
name: compare-travel-times
description: Compare travel time and distance from several origins to several destinations and pick the closest or fastest option. Use when asked which depot, store, technician or office is nearest, or to build a travel-time table.
---

# Compare Travel Times

## When to use this skill

- The user has a set of starting points and a set of targets and wants to know which pairing is fastest or shortest.
- Cue phrases: "which warehouse is closest to these customers", "nearest technician", "travel time table", "who can get there first".

## How to do it

1. Collect coordinates (longitude, latitude) for every origin and every destination. The tool does not geocode, so ask for coordinates or use another tool to look them up.
2. Check the size. Origins multiplied by destinations must not exceed 2,500, and matrices over 100 pairs have extra restrictions. If the call is rejected for size, split into batches and tell the user.
3. Call `get_route_matrix` with `origins` and `destinations`.
   - Set `travelMode` to `truck` or `walking` when it is not driving.
   - Set `traffic` to `live` for current conditions or leave it as `historical`.
   - Set `departAt` or `arriveAt` (ISO 8601) for a specific time. Use only one.
4. Present a table with one row per origin and one column per destination showing travel time and distance. Highlight the fastest option for each origin or destination, depending on what the user asked.

## What to watch for

- Times are in seconds and distances in meters; convert for the user.
- If the service returns no result for a pair, list that pair as unreachable instead of omitting it.
- For the actual route between one pair, use the plan-a-route skill.
