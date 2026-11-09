---
name: show-a-map
description: Show places, routes, areas, boundaries, live traffic or a weather radar on an interactive map, or make a map image. Use when asked to map something, show a boundary, or show the radar.
---

# Show a Map

## When to use this skill

- "Map these five offices", "show the radar over Florida", "draw the boundary of Bellevue", "give me a map image for a report".

## How to do it

1. For pins, lines, areas and circles call `show_map`. Give `markers` (addresses or coordinates, with optional `label` and `description`), `lines`, `polygons` or `circles`. Set `style` (`road`, `dark`, `satellite`, `hybrid`, `terrain`) and `showTraffic` if wanted.
2. For a weather radar call `show_weather_radar` with `center` and `layer` (`radar` or `infrared`). It animates the last 90 minutes and the next 2 hours.
3. For the boundary of a city, postal code, county, state or country call `get_boundary` with a point inside it and `resultType`.
4. For a picture to paste into a document call `get_static_map_image`. Leave the size and format at their defaults.
5. For a route on a map use the plan-a-route skill.

## What to watch for

- If the user cannot see the interactive map, list the places in text. Never draw a map yourself or describe one as attached.
- Pin labels on the map are short; put the full name in `description`.
