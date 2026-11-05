---
name: map-a-route
description: Show a route, a set of places, or an area on a static map image. Use when asked to show the route on a map, draw directions, map these stops, or produce a map picture for a message or report.
---

# Map a Route

## When to use this skill

- The user wants a picture of a route, stops, or a service area.
- Cue phrases: "show me the route on a map", "map these three stops", "give me a map I can paste into an email".

## How to do it

1. For a route, call `get_route_map` once. It gets the directions and shows the route on a map in one step, returning the distance and travel time, a Bing Maps link, and a map widget. Pass the origin and destination as longitude and latitude, and any stops as `waypoints` text (`longitude,latitude` pairs separated by semicolons). Don't call `get_route_directions` first; it isn't needed and adds a lot of data.
2. For places, call `get_static_map` with `centerLongitude`, `centerLatitude`, and `zoom` (about 12 for a city, 15 for a neighborhood), and add one `pins` item per place.
3. For an area, call `get_static_map` with `bbox` as text: minimum longitude, minimum latitude, maximum longitude, maximum latitude.
4. Leave the image size and format at their defaults (a small JPEG), and don't set `embedImage`. Set `tilesetId` to `microsoft.imagery` for satellite or `microsoft.base.darkgrey` for a dark map, and `trafficLayer` to `microsoft.traffic.relative.main` to show live traffic.
5. In clients that support MCP Apps, a map widget shows the map to the user. In other clients there is no image, so rely on the Bing Maps link. Either way, give the distance and travel time and share the Bing Maps link from the result.

## What to watch for

- Coordinates are longitude first, then latitude.
- Long routes are simplified for drawing. The map is a picture, not the exact route.
- Don't say an image is attached. If the user can't see a map, share the Bing Maps link and give the distance and time.
- If the Azure Maps tools aren't available in this session, say so and stop. Never draw or generate a map image, or use another mapping service, to stand in for one.
- To get the route itself, use the plan-a-route skill.
