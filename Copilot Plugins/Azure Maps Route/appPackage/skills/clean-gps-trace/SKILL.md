---
name: clean-gps-trace
description: Snap noisy GPS points to the road network and return the matched route as GeoJSON, optionally with speed limits. Use when asked to clean up a vehicle track, match GPS points to roads, or check speed limits along a trip.
---

# Clean a GPS Trace

## When to use this skill

- The user has GPS points from a vehicle or device and wants them aligned to roads.
- Cue phrases: "snap these points to the road", "clean up this track", "what were the speed limits on this trip".

## How to do it

1. Make sure the points are in travel order, each as longitude and latitude.
2. Call `snap_to_roads` with `points`, written as text: `longitude,latitude` pairs separated by semicolons.
   - Set `interpolate` to `true` to add points along the road between the snapped points, which gives a smoother line.
   - Set `includeSpeedLimit` to `true` to get speed limits in kilometers per hour.
   - Set `travelMode` to `truck` for commercial vehicles.
3. Return the GeoJSON FeatureCollection, and summarize how many points were matched and any notable speed limits when requested.

## What to watch for

- If the response has fewer matched points than you sent, tell the user rather than assuming every point was snapped.
- Sparse or out-of-order points give poor matches. If results look wrong, ask whether the points are in travel order.
