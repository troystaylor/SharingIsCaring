---
name: plan-a-route
description: Get directions between places, with stops, alternatives and live traffic, and show them on a map. Use when asked how to get somewhere, how long a drive or walk takes, or for turn-by-turn directions.
---

# Plan a Route

## When to use this skill

- "How do I get from… to…", "how long to drive to…", "directions with a stop at…", "avoid tolls".

## How to do it

1. Call `get_route_directions` with `origin` and `destination` as text or coordinates, and `waypoints` for stops in order.
2. Set `travelMode` to `driving`, `truck` or `walking`. For a truck, fill `vehicle` with the dimensions and weight the user gives. Set `avoid` for tolls, ferries, highways and so on.
3. For traffic, set `optimizeRoute` to `fastestWithTraffic`. For a future trip, also set `departAt` or `arriveAt` as ISO 8601 with a UTC offset.
4. Set `units` to `imperial` when the user asks for miles, and `metric` for kilometers.
5. Tell the user the distance and time of the best route and how the alternatives differ. Offer the turn-by-turn steps if they want them.
6. Use `get_route_directions_advanced` only when the user needs a mode or option that `get_route_directions` doesn't have: bicycle, motorcycle, bus, van or taxi, eco or thrilling routes, or electric or combustion fuel use.
7. For many separate trips at once, use `get_route_directions_batch`. For the best time to leave, use the best-time-to-leave skill.

## What to watch for

- In clients that support MCP Apps, a route planner shows the routes, and the user can change the mode and options there. In other clients, share the summary and the Bing Maps link from the result.
- Don't draw a map yourself or use another mapping service.
- Times with traffic are predictions.
