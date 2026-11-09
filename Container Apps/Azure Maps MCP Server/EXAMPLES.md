# Azure Maps MCP server: examples

Example prompts and tool calls for every tool and every app. Use the prompts in a client such as Microsoft 365 Cowork with the [Azure Maps plugin](../../Copilot%20Plugins/Azure%20Maps/README.md). Use the JSON to call a tool directly, for example in the [local preview](./README.md#run-locally) (pick the tool, paste the JSON and press **Run**) or with `node test/call.js <tool> '<json>'`.

Places can be text or coordinates in every tool that takes a place:

```json
{ "query": "Space Needle, Seattle" }
{ "longitude": -122.3493, "latitude": 47.6205, "name": "Space Needle" }
```

Coordinates are always longitude first, then latitude. Dates and times in examples are samples: use a future date for traffic and departure predictions.

## The apps

| App | Try saying | Opens from |
| --- | --- | --- |
| [Interactive map](#interactive-map) | "Map our five offices with the 800 m walking radius around HQ." | `show_map`, `show_weather_radar`, `get_boundary`, `get_polygons`, `snap_to_roads`, `get_route_range_batch` |
| [Place finder](#place-finder) | "Find coffee shops near Pike Place Market that are open now." | `geocode`, `autocomplete_places`, `reverse_geocode`, every `search_*` tool |
| [Route planner](#route-planner) | "Directions from Seattle Center to Microsoft Redmond with live traffic, in miles." | `get_route_directions`, `get_route_directions_advanced` |
| [Reachable area](#reachable-area) | "What can I reach from our store in 10, 20 and 30 minutes, and which pharmacies are inside?" | `get_route_range`, `get_reachable_range_advanced` |
| [Travel time matrix](#travel-time-matrix) | "Which of our three depots is closest to each customer?" | `get_route_matrix`, `get_route_operation` |
| [Best time to leave](#best-time-to-leave) | "When should I leave Bellevue for Seattle Center tomorrow morning?" | `compare_departure_times` |
| [Weather dashboard](#weather-dashboard) | "What's the weather in Seattle?" | `get_weather` |
| [Weather along the route](#weather-along-the-route) | "What's the weather on the drive from Seattle to Portland?" | `get_weather_along_route` |
| [Traffic](#traffic) | "Any accidents or closures around Seattle?" | `get_traffic_incidents`, `get_traffic_flow_segment` |
| [World clock and meeting planner](#world-clock-and-meeting-planner) | "Find a meeting time for Seattle, London and Mumbai." | `get_timezone` |
| [Batch geocoding](#batch-geocoding) | "Geocode these 12 addresses and tell me which didn't match." | `batch_geocode`, `batch_reverse_geocode` |
| [Storm tracker](#storm-tracker) | "Are there any hurricanes or typhoons right now?" | `get_tropical_storms` |

### Interactive map

- "Show a map of Space Needle and Pike Place Market with a 500 m circle around Seattle Center."
- "Show the weather radar over Florida." The radar animates the last 90 minutes and the next 2 hours.
- "Draw the boundary of Bellevue, WA."
- "Snap this GPS trace to the road and color it by speed limit."

In the app: switch Road, Dark, Satellite, Hybrid or Terrain, toggle Traffic, Weather radar and Infrared, click a legend entry to hide a shape, click a pin for its details.

### Place finder

- "Find a pharmacy near Seattle Center."
- "Where is 1 Microsoft Way, Redmond?"
- "What's the address at 47.6205, -122.3493?"

In the app: type in the box for suggestions, sort by relevance or distance, select a place for hours, phone and directions, pan the map and press **Search this area**.

### Route planner

- "Directions from Seattle Center to Microsoft Redmond with a stop in Bellevue, avoiding tolls."
- "How long to walk from Pike Place Market to the Space Needle?"
- "Route a 4 m tall truck from Seattle to Spokane."

In the app: select an alternative route, change the mode (Drive, Truck, Walk), preference and avoid options, swap start and end, click a step to see it on the map, copy or save the directions.

### Reachable area

- "What's within a 15 minute drive of Seattle Center?"
- "How far can I get on 30 kWh in an electric car from Seattle?"

In the app: choose time limits, change the mode, press **Move start** and click the map, search for places inside (Pharmacy, Grocery, Coffee, Fuel, EV charging, Hospital).

### Travel time matrix

- "Build a travel time table from Seattle, Bellevue and Everett to Redmond, Tacoma and Olympia."

In the app: switch Time, Distance or With traffic, sort by a row or column, read the nearest origin for each destination, hover a cell to see the pair on the map, download or copy the CSV.

### Best time to leave

- "Compare leaving Bellevue for Seattle Center every 30 minutes from 6 to 10 AM tomorrow."
- "I need to arrive by 9 AM. When should I leave?"

In the app: change the date, window, step, Depart or Arrive, and mode, then **Compare**. Open **Show table** for the numbers.

### Weather dashboard

- "What's the weather in Seattle in Fahrenheit?"
- "Will it rain in the next two hours in Portland?"

In the app: switch Metric and Imperial, search another place, switch the hourly chart between Temperature, Precipitation and Wind, expand a day or an alert.

### Weather along the route

- "Will I hit rain on the drive from Seattle to Portland if I leave in 30 minutes?"

In the app: choose Now, +15, +30, +60 or +90 minutes, switch °C and °F, select a point on the route or in the timeline.

### Traffic

- "Show incidents within 15 km of Seattle."
- "How fast is traffic moving on I-5 near downtown Seattle?"

In the app: filter by incident type, select an incident, click the map for the speed on that road, press **Search this area** after moving the map.

### World clock and meeting planner

- "What time is it in Tokyo, London and New York, and when do the clocks change in London?"

In the app: switch 12 and 24 hour, set the home zone, preview another date, drag the planner, add a place, copy the invite text.

### Batch geocoding

- "Geocode these addresses: 1 Microsoft Way Redmond WA; 400 Broad St Seattle; 12 Nowhere Lane Zzyzx."
- "Give me the addresses for these 20 coordinates."

In the app: filter by confidence, sort the table, press **Fix** on a row that didn't match (type an address, or "latitude, longitude" in reverse mode), **Retry all unmatched**, download the CSV.

### Storm tracker

- "Show the track of the active storm in the central Pacific."
- "What Atlantic hurricanes were there in 2024?"

In the app: select a storm for its forecast table and wind radii, switch Metric and Imperial, search by year and basin when none are active.

## Every tool

### Geocoding

### `geocode`
"Where is the Space Needle?"
```json
{ "query": "Space Needle Seattle" }
```
Structured form (use either `query` or the parts, not both):
```json
{ "addressLine": "400 Broad St", "locality": "Seattle", "adminDistrict": "WA", "countryRegion": "US" }
```

### `autocomplete_places`
"Suggest addresses starting with '1 Micro' near Redmond."
```json
{ "query": "1 Micro", "near": { "longitude": -122.12, "latitude": 47.64 }, "top": 5 }
```

### `reverse_geocode`
"What's the address at 47.6205, -122.3493?"
```json
{ "location": { "longitude": -122.3493, "latitude": 47.6205 } }
```

### `batch_geocode`
"Geocode these addresses and tell me which matched."
```json
{ "addresses": ["1 Microsoft Way, Redmond, WA", "400 Broad St Seattle", "12 Nowhere Lane Zzyzx"] }
```

### `batch_reverse_geocode`
"Give me the addresses for these points."
```json
{ "points": [{ "longitude": -122.33, "latitude": 47.6 }, { "longitude": 2.2945, "latitude": 48.8584 }] }
```

### `get_boundary`
"Draw the boundary of Bellevue, WA."
```json
{ "location": { "query": "Bellevue WA" }, "resultType": "locality", "resolution": "small" }
```

### Search

### `search_fuzzy`
"Find coffee near Pike Place Market."
```json
{ "query": "coffee", "near": { "query": "Pike Place Market Seattle" }, "limit": 5 }
```

### `search_poi`
"Where are the Starbucks near downtown Seattle? Include opening hours."
```json
{ "query": "starbucks", "near": { "query": "Seattle" }, "limit": 5, "openingHours": "nextSevenDays" }
```

### `search_poi_category`
"Find EV charging stations near Bellevue."
```json
{ "query": "ELECTRIC_VEHICLE_STATION", "near": { "query": "Bellevue WA" }, "limit": 10 }
```

### `search_nearby`
"What's within 1 km of the Space Needle?"
```json
{ "location": { "query": "Space Needle Seattle" }, "radius": 1000, "limit": 10 }
```

### `search_structured_address`
"Find 400 Broad Street, Seattle, as separate fields."
```json
{ "countryCode": "US", "streetNumber": "400", "streetName": "Broad St", "municipality": "Seattle", "countrySubdivision": "WA" }
```

### `search_cross_street`
"What's the nearest intersection to this point?"
```json
{ "location": { "longitude": -122.3493, "latitude": 47.6205 } }
```

### `search_inside_geometry`
"Find pizza places inside a 600 m circle around Pike Place Market."
```json
{ "query": "pizza", "center": { "query": "Pike Place Market Seattle" }, "radiusMeters": 600, "limit": 5 }
```

### `search_along_route`
"Find a coffee stop on the drive from Seattle to Tacoma with at most a 10 minute detour."
```json
{ "query": "coffee", "origin": { "query": "Seattle" }, "destination": { "query": "Tacoma WA" }, "maxDetourTimeSeconds": 600, "limit": 5 }
```

### `get_poi_categories`
"What's the category ID for EV charging?"
```json
{ "filter": "electric" }
```

### `get_polygons`
"Draw the polygon for that result." Use the `geometryId` from a search result, for example from `search_fuzzy` with `"entityType": "Municipality"`.
```json
{ "geometries": ["<geometryId from a search result>"], "zoom": 12 }
```

### `batch_search_fuzzy`
"Look up these place names in one go."
```json
{ "queries": ["Space Needle", "Pike Place Market", "Museum of Flight"], "limit": 1 }
```

### Routing

### `get_route_directions`
"Directions from Seattle Center to Microsoft Redmond with traffic, avoiding tolls."
```json
{ "origin": { "query": "Seattle Center" }, "destination": { "query": "Microsoft Redmond campus" }, "optimizeRoute": "fastestWithTraffic", "avoid": ["tollRoads"], "units": "imperial" }
```
With a stop and a truck:
```json
{ "origin": { "query": "Seattle" }, "waypoints": [{ "query": "Ellensburg WA" }], "destination": { "query": "Spokane WA" }, "travelMode": "truck", "vehicle": { "heightMeters": 4, "weightKg": 20000, "commercial": true } }
```

### `get_route_directions_batch`
"Time these two trips."
```json
{ "trips": [{ "id": "a", "origin": { "query": "Seattle" }, "destination": { "query": "Tacoma WA" } }, { "id": "b", "origin": { "query": "Bellevue WA" }, "destination": { "query": "Redmond WA" } }] }
```

### `get_route_matrix`
"How long from each office to each customer?"
```json
{ "origins": [{ "query": "Seattle" }, { "query": "Bellevue WA" }], "destinations": [{ "query": "Redmond WA" }, { "query": "Tacoma WA" }, { "query": "Everett WA" }] }
```

### `start_route_matrix_async`
"Start a big matrix; I'll check on it." Returns an operation ID for `get_route_operation`.
```json
{ "origins": [{ "query": "Seattle" }, { "query": "Bellevue WA" }], "destinations": [{ "query": "Tacoma WA" }, { "query": "Everett WA" }] }
```

### `get_route_operation`
"Is that matrix done?"
```json
{ "operationId": "<id from start_route_matrix_async>" }
```

### `get_route_range`
"What can I reach in 10 and 20 minutes from Seattle Center, and where are the pharmacies?"
```json
{ "origin": { "query": "Seattle Center" }, "timeBudgetsMinutes": [10, 20], "placesQuery": "pharmacy" }
```

### `get_route_range_batch`
"Show the 10 minute drive area of each store."
```json
{ "origins": [{ "query": "Seattle" }, { "query": "Bellevue WA" }], "timeBudgetInSec": 600 }
```

### `snap_to_roads`
"Snap this GPS trace to the road and show speed limits."
```json
{ "points": [{ "longitude": -122.33, "latitude": 47.6 }, { "longitude": -122.325, "latitude": 47.604 }, { "longitude": -122.32, "latitude": 47.608 }], "interpolate": true, "includeSpeedLimit": true }
```

### `snap_to_roads_batch`
"Snap these two traces."
```json
{ "traces": [{ "id": "trip1", "points": [{ "longitude": -122.33, "latitude": 47.6 }, { "longitude": -122.325, "latitude": 47.604 }] }, { "id": "trip2", "points": [{ "longitude": -122.2, "latitude": 47.61 }, { "longitude": -122.195, "latitude": 47.615 }] }] }
```

### `compare_departure_times`
"When should I leave Bellevue for Seattle Center tomorrow morning?"
```json
{ "origin": { "query": "Bellevue WA" }, "destination": { "query": "Seattle Center" }, "date": "2026-11-10", "startTime": "06:00", "endTime": "10:00", "stepMinutes": 60 }
```
To arrive by a deadline:
```json
{ "origin": { "query": "Bellevue WA" }, "destination": { "query": "Seattle Center" }, "date": "2026-11-10", "startTime": "07:30", "endTime": "09:30", "stepMinutes": 30, "timeMode": "arriveAt" }
```

### `get_route_directions_advanced`
"Cycling directions from Seattle to Bellevue."
```json
{ "origin": { "query": "Seattle" }, "destination": { "query": "Bellevue WA" }, "travelMode": "bicycle", "maxAlternatives": 1 }
```

### `get_reachable_range_advanced`
"How far can an electric car go from Seattle on 30 kWh?"
```json
{ "origin": { "query": "Seattle" }, "energyBudgetInkWh": 30, "vehicleEngineType": "electric", "constantSpeedConsumptionInkWhPerHundredkm": "50,8.2:130,21.3", "currentChargeInkWh": 30, "maxChargeInkWh": 60 }
```

### Weather

### `get_weather`
"What's the weather in Seattle?"
```json
{ "location": { "query": "Seattle" }, "unit": "imperial" }
```

### `get_current_conditions`
"What's it like outside in Seattle right now?"
```json
{ "location": { "query": "Seattle" } }
```

### `get_hourly_forecast`
"Hour by hour for the next 12 hours in Seattle."
```json
{ "location": { "query": "Seattle" }, "duration": "12" }
```

### `get_daily_forecast`
"Five day forecast for Seattle."
```json
{ "location": { "query": "Seattle" }, "duration": "5", "unit": "imperial" }
```

### `get_quarter_day_forecast`
"Morning, afternoon, evening and night for tomorrow."
```json
{ "location": { "query": "Seattle" }, "duration": "1" }
```

### `get_minute_forecast`
"Will it rain in the next two hours?"
```json
{ "location": { "query": "Seattle" }, "interval": "15" }
```

### `get_severe_weather_alerts`
"Any weather alerts for Oklahoma City?"
```json
{ "location": { "query": "Oklahoma City" } }
```

### `get_current_air_quality`
"How's the air quality in Seattle?"
```json
{ "location": { "query": "Seattle" } }
```

### `get_air_quality_hourly`
"Air quality over the next 12 hours."
```json
{ "location": { "query": "Seattle" }, "duration": "12" }
```

### `get_air_quality_daily`
"Air quality for the next three days."
```json
{ "location": { "query": "Seattle" }, "duration": "3" }
```

### `get_daily_indices`
"Is it a good day for a run?"
```json
{ "location": { "query": "Seattle" }, "duration": "1" }
```

### `get_historical_weather`
"What's normal for the first three days of November in Seattle?"
```json
{ "location": { "query": "Seattle" }, "kind": "normals", "startDate": "2026-11-01", "endDate": "2026-11-03" }
```
Use `"kind": "actuals"` for what happened and `"records"` for record highs and lows.

### `get_tropical_storms`
"Are there any hurricanes or typhoons active right now?"
```json
{}
```
A past season:
```json
{ "scope": "search", "year": 2024, "basinId": "AL" }
```
One storm's track:
```json
{ "scope": "track", "year": 2026, "basinId": "CP", "govId": 4 }
```

### `get_weather_along_route`
"What's the weather on the drive from Seattle to Portland if I leave in 30 minutes?"
```json
{ "origin": { "query": "Seattle" }, "destination": { "query": "Portland OR" }, "leaveInMinutes": 30 }
```

### Traffic

### `get_traffic_incidents`
"Any accidents or closures within 15 km of Seattle?"
```json
{ "location": { "query": "Seattle" }, "radiusKm": 15 }
```
Only accidents and road hazards:
```json
{ "location": { "query": "Seattle" }, "radiusKm": 15, "incidentTypes": ["Accident", "RoadHazard"] }
```

### `get_traffic_flow_segment`
"How fast is traffic moving on I-5 near downtown Seattle?"
```json
{ "location": { "query": "I-5 Seattle" }, "zoom": 12 }
```

### `get_traffic_incident_detail`
"Give me the legacy incident records for downtown Seattle." (`get_traffic_incidents` is simpler.)
```json
{ "bbox": "-122.4,47.55,-122.25,47.65" }
```

### `get_traffic_tile`
"Get the traffic flow tile for this map square."
```json
{ "zoom": 12, "x": 655, "y": 1430 }
```

### Time and place

### `get_timezone`
"What time is it in Seattle and London, and when is the best meeting time?"
```json
{ "locations": [{ "query": "Seattle" }, { "query": "London" }, { "query": "Mumbai" }], "workStart": "08:00", "workEnd": "18:00" }
```
By zone ID:
```json
{ "timezoneIds": ["Asia/Kolkata", "Pacific/Auckland"] }
```

### `list_timezones`
"List the Windows time zones that mention Pacific."
```json
{ "kind": "windows", "filter": "Pacific", "limit": 5 }
```

### `get_iana_version`
"Which time zone database version is in use?"
```json
{}
```

### `convert_windows_timezone`
"What's the IANA name for Pacific Standard Time?"
```json
{ "windowsId": "Pacific Standard Time" }
```

### `get_ip_location`
"Which country is 8.8.8.8 in?"
```json
{ "ip": "8.8.8.8" }
```

### Maps

### `show_map`
"Map the Space Needle and Pike Place Market with a 500 m circle around Seattle Center."
```json
{ "title": "Seattle", "markers": [{ "query": "Space Needle Seattle" }, { "query": "Pike Place Market Seattle", "label": "P", "description": "Market" }], "circles": [{ "name": "500 m", "center": { "query": "Seattle Center" }, "radiusMeters": 500 }], "showTraffic": true }
```

### `show_weather_radar`
"Show the weather radar over Miami."
```json
{ "center": { "query": "Miami FL" }, "zoom": 6, "layer": "radar" }
```

### `get_static_map_image`
"Give me a map image of downtown Seattle with the Space Needle marked."
```json
{ "center": { "query": "Seattle" }, "zoom": 12, "markers": [{ "query": "Space Needle Seattle", "label": "A" }], "showTraffic": true }
```

### `get_map_tile`
"Get one satellite tile."
```json
{ "tilesetId": "microsoft.imagery", "zoom": 10, "x": 164, "y": 358 }
```

### `get_map_tileset`
"What zoom levels does the road tileset cover?"
```json
{ "tilesetId": "microsoft.base.road" }
```

### `get_map_attribution`
"What attribution do I need to show for this map area?"
```json
{ "zoom": 10, "bounds": "-122.4,47.5,-122.2,47.7" }
```

### `get_copyright`
"Give me the copyright caption for the map."
```json
{ "scope": "caption" }
```

## Mixed prompts that use several tools

- "I'm driving from Seattle to Portland tomorrow. Compare leaving at 7, 8 and 9 AM, check the weather on the way, and find a charger along the route." Uses `compare_departure_times`, `get_weather_along_route` and `search_along_route`.
- "Our offices are in Seattle, Bellevue and Tacoma. Which is closest to each of these 10 customer addresses, and how far can each office reach in 20 minutes?" Uses `batch_geocode`, `get_route_matrix` and `get_route_range_batch`.
- "Plan a team call across Seattle, London and Mumbai, then tell me the weather in each city." Uses `get_timezone` and `get_weather`.
- "Is the road to the pass open, and what are conditions there?" Uses `get_traffic_incidents`, `get_severe_weather_alerts` and `get_current_conditions`.
