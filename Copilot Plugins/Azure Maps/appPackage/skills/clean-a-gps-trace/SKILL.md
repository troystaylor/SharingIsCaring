---
name: clean-a-gps-trace
description: Snap noisy GPS points to the road network and get the matched route and speed limits. Use when asked to clean up, match or analyze a GPS track or vehicle trace.
---

# Clean a GPS Trace

## When to use this skill

- "Snap these GPS points to the road", "what speed limits were on this trip", "reconstruct the route from this track".

## How to do it

1. Call `snap_to_roads` with `points` in travel order (at least 2, up to 5,000) as longitude and latitude. Consecutive points must be within 6 km of each other.
2. Set `interpolate` to add points along the road between the matched points, and `includeSpeedLimit` for speed limits in km/h.
3. Report how many points matched, the roads used, and any points that could not be matched.
4. For many separate traces use `snap_to_roads_batch` (up to 100 traces of up to 100 points).

## What to watch for

- Coordinates are longitude first, then latitude.
- In clients that support MCP Apps, a map shows the raw trace against the snapped route, colored by speed limit.
- Matching is best with points recorded every few seconds.
