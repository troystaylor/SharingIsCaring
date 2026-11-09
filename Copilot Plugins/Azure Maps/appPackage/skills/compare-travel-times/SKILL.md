---
name: compare-travel-times
description: Compare travel times and distances between many origins and destinations, for example the closest depot for each customer. Use when asked which location is nearest, to assign deliveries, or for a distance table.
---

# Compare Travel Times

## When to use this skill

- "Which of our three offices is closest to each customer?", "how long from each store to each warehouse", "build a travel time table".

## How to do it

1. Call `get_route_matrix` with `origins` and `destinations`. Each is a list of places given as text or coordinates.
2. Origins times destinations must not exceed 2,500. For larger matrices call `start_route_matrix_async`, then call `get_route_operation` with the returned ID until the status is `Completed`.
3. Set `travelMode`, and `traffic` to `live` or `historical`. Set `departAt` to compare a specific time.
4. Summarize: the nearest origin for each destination, the farthest pairs, and any pair with no route.
5. For separate trips that don't form a grid, use `get_route_directions_batch` instead (up to 100).

## What to watch for

- The matrix gives straight travel times, not an optimized delivery order.
- In clients that support MCP Apps, a heat-map table shows the matrix with the nearest origin for each destination and a CSV download.
- Offer a CSV or table the user can paste into Excel.
