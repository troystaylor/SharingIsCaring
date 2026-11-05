---
name: plan-a-route
description: Plan a driving, truck or walking route between two places with optional stops and get distance, travel time and the route line as GeoJSON. Use when asked for directions, how long a trip takes, the best order for a set of stops, or a route to show on a map.
---

# Plan a Route

## When to use this skill

- The user wants directions or an ETA between an origin and a destination, optionally through stops.
- Cue phrases: "how long to drive from A to B", "route for a delivery truck", "best order for these stops", "give me the route as GeoJSON".

## How to do it

1. Get coordinates for every place as longitude and latitude. If the user gives addresses or place names, ask for coordinates or use another tool that looks them up; `get_route_directions` does not geocode. Remember the order is longitude first, then latitude.
2. Call `get_route_directions` with the origin, destination and any `waypoints` in order, written as text: `longitude,latitude` pairs separated by semicolons.
   - Set `travelMode` to `truck` for commercial vehicles and `walking` for pedestrians.
   - Set `optimizeRoute` to `fastestWithTraffic` for live traffic, or `shortest` for least distance.
   - Set `departAt` or `arriveAt` (ISO 8601) when the user cares about a specific time. Use only one.
   - Set `optimizeWaypointOrder` to `true` when the user wants the best order for the stops.
   - Leave `routeOutputOptions` at its default (distance and time). Add `routePath` only when the user wants the route line as GeoJSON, and `itinerary` only when they ask for turn-by-turn directions; both make the response much larger.
   - Leave `maxRouteCount` unset (one route) unless the user asks for alternatives. Each extra route adds a lot of data.
3. Report the total distance and travel time from the route summary, and the order the stops are visited (the optimized order when requested). Share the Bing Maps link from the result so the user can open the route.
4. If the user wants the geometry, ask for `routePath` and return the GeoJSON FeatureCollection as returned. Do not reformat the coordinates. To show a route as a picture, use the map-a-route skill.

## What to watch for

- Distances are in meters and times are in seconds in the response; convert for the user.
- Use only values the tool returned. If the call fails, tell the user the error and do not guess a route.
- If the Azure Maps tools aren't available in this session, say so and stop. Don't estimate a route, draw or generate a map image, or use another mapping service in its place.
- To compare many origins and destinations, use the compare-travel-times skill instead.
