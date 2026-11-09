---
name: find-places
description: Find an address, a business, a landmark or a type of place, or turn coordinates into an address. Use when asked where something is, what is nearby, to find a restaurant, pharmacy, charging station or similar, or what is at a location.
---

# Find Places

## When to use this skill

- The user asks "where is…", "find a pharmacy near…", "what's near the Space Needle", "what address is at these coordinates".

## How to do it

1. Pick the tool:
   - A named place or business: `search_poi`. A kind of place ("coffee", "pharmacy"): `search_poi` or `search_fuzzy`.
   - A category such as `RESTAURANT`, `PETROL_STATION` or `ELECTRIC_VEHICLE_STATION`: `search_poi_category`. Use `get_poi_categories` with a `filter` to look up names or IDs.
   - Everything around a point, nearest first: `search_nearby`.
   - A street address: `geocode`. Check the `confidence`; a Low match may be a nearby or larger area, so say so.
   - Coordinates to an address: `reverse_geocode`.
   - An address given as separate parts, with a country code: `search_structured_address`. The nearest intersection to a point: `search_cross_street`. Places inside a drawn circle or polygon: `search_inside_geometry`.
   - Suggestions while the user types: `autocomplete_places`.
2. Give places as text (`{"query": "Pike Place Market, Seattle"}`) or as longitude and latitude. Use `near` to bias results to where the user is or is interested in. Don't geocode first; the tools do it for you.
3. Set `limit` to what the user needs (5 to 10 is usually enough). Set `openingHours` to `nextSevenDays` when hours matter.
4. In clients that support MCP Apps, the results appear on a map with a list the user can browse and a "search this area" button. Summarize the top results in text as well.

## What to watch for

- Coordinates are longitude first, then latitude.
- If the user cannot see the map, list the places with their addresses. Never invent a place that isn't in the results.
- If the Azure Maps tools aren't available in this session, say so and stop. Don't answer from memory.
