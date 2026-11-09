# Azure Maps MCP server

A hosted [Model Context Protocol](https://modelcontextprotocol.io) server for the whole Azure Maps REST API: geocoding and search, routing, weather, traffic, time zones, map rendering and IP geolocation. It also ships **twelve interactive MCP Apps** (maps, a route planner, a weather dashboard, a travel time matrix, and more) that appear inline in clients that support MCP Apps, such as Microsoft 365 Copilot and Cowork.

It runs on Azure Container Apps. Callers sign in with Microsoft Entra. The server validates the user's access token and calls Azure Maps with its own managed identity, so no Azure Maps key is stored or sent anywhere.

This is the full-API companion to [Azure Maps Route](../Azure%20Maps%20Route/README.md), which covers routing only. A matching [Copilot plugin](../../Copilot%20Plugins/Azure%20Maps/README.md) with 14 skills is ready to share; it uses placeholders for your server URL and OAuth registration.

For ready-to-paste prompts and JSON arguments for every app and tool, see [EXAMPLES.md](EXAMPLES.md).

## What it can do

Most tools accept a place as **text** (an address or a name) or as **coordinates**, so an agent can ask for "directions from Seattle Center to Microsoft Redmond" in one call. Text is geocoded once per call and reused.

| Area | Tools |
| --- | --- |
| Geocoding | `geocode`, `autocomplete_places`, `reverse_geocode`, `batch_geocode`, `batch_reverse_geocode`, `get_boundary` |
| Search | `search_fuzzy`, `search_poi`, `search_poi_category`, `search_nearby`, `search_structured_address`, `search_cross_street`, `search_inside_geometry`, `search_along_route`, `get_poi_categories`, `get_polygons`, `batch_search_fuzzy` |
| Routing | `get_route_directions`, `get_route_directions_batch`, `get_route_matrix`, `start_route_matrix_async`, `get_route_operation`, `get_route_range`, `get_route_range_batch`, `snap_to_roads`, `snap_to_roads_batch`, `compare_departure_times`, `get_route_directions_advanced`, `get_reachable_range_advanced` |
| Weather | `get_weather`, `get_current_conditions`, `get_hourly_forecast`, `get_daily_forecast`, `get_quarter_day_forecast`, `get_minute_forecast`, `get_severe_weather_alerts`, `get_current_air_quality`, `get_air_quality_hourly`, `get_air_quality_daily`, `get_daily_indices`, `get_historical_weather`, `get_tropical_storms`, `get_weather_along_route` |
| Traffic | `get_traffic_incidents`, `get_traffic_flow_segment`, `get_traffic_incident_detail`, `get_traffic_tile` |
| Time and place | `get_timezone`, `list_timezones`, `get_iana_version`, `convert_windows_timezone`, `get_ip_location` |
| Maps | `show_map`, `show_weather_radar`, `get_static_map_image`, `get_map_tile`, `get_map_tileset`, `get_map_attribution`, `get_copyright` |

### Which API versions it uses

| Service | Version | Notes |
| --- | --- | --- |
| Search (geocode, reverse geocode, autocomplete, batch, polygon) | 2026-01-01 | Replaces the Search 1.0 equivalents |
| Search (fuzzy, POI, nearby, category, structured, cross street, inside geometry, along route, category tree, batch fuzzy) | 1.0 | The 2026-01-01 version has no equivalent for these |
| Route (directions, matrix, async matrix, range, snap to roads, and their batches) | 2025-01-01 | |
| Route (`get_route_directions_advanced`, `get_reachable_range_advanced`) | 1.0 | Adds bicycle, motorcycle, bus, van and taxi modes, eco and thrilling routes, and electric and combustion range |
| Weather | 1.1 | |
| Traffic (incidents) | 2025-01-01 | |
| Traffic (flow segment, legacy incident detail, tiles) | 1.0 | |
| Render | 2024-04-01 | |
| Time zone, Geolocation | 1.0 | |

Azure Maps Creator, Mobility (transit) and Feature State are retired, and Elevation and Spatial are not part of the current REST API reference, so none of them are included.

## The apps

The apps are MCP Apps: single-page widgets the host renders in a sandboxed frame. They load nothing from the network. Map tiles, searches and re-routing all go through the host, which calls this server's tools with the user's sign-in. That is what makes the apps interactive: the user pans a map, drags a slider or picks a result, and the app asks the server for more.

| App | Opens from | What the user can do |
| --- | --- | --- |
| **Interactive map** | `show_map`, `show_weather_radar`, `get_boundary`, `get_polygons`, `snap_to_roads`, `get_route_range_batch` | Pan and zoom a real map with road, dark, satellite, hybrid and terrain styles. Show live traffic. Play an animated weather radar or infrared layer covering the last 90 minutes and the next 2 hours. Toggle shapes. Snapped GPS traces are colored by speed limit. |
| **Place finder** | All search and geocoding tools | Browse results on a map and list, sort by distance, see opening hours, search as you type, and **search this area** after moving the map. Selecting a place tells the agent what the user picked. |
| **Route planner** | `get_route_directions`, `get_route_directions_advanced` | Compare alternative routes, switch between drive, truck and walk, prefer traffic, shortest or fastest, avoid tolls, highways or ferries, swap start and end, and walk through the turn-by-turn steps on the map. |
| **Reachable area** | `get_route_range`, `get_reachable_range_advanced` | See nested 5 to 60 minute areas, move the start point, change the travel mode, and find places inside the area (pharmacies, EV chargers and so on). |
| **Travel time matrix** | `get_route_matrix`, `get_route_operation` | A heat-map table of times between every origin and destination, the nearest origin for each destination, a load balance view, and CSV export. |
| **Best time to leave** | `compare_departure_times` | A chart of predicted travel time across the day, with the best and worst times to leave or arrive. |
| **Weather dashboard** | `get_weather` | Current conditions, a two-hour rain forecast, an hourly chart, a five-day forecast, severe weather alerts and air quality. |
| **Weather along the route** | `get_weather_along_route` | The route colored by weather hazard, with a timeline and a "leave in" selector. |
| **Traffic** | `get_traffic_incidents`, `get_traffic_flow_segment` | Incidents by type and severity on a live traffic map, and the current speed on any road the user taps. |
| **World clock and meeting planner** | `get_timezone` | Live clocks with daylight hours, daylight saving changes, and the working hours every participant shares. |
| **Batch geocoding** | `batch_geocode`, `batch_reverse_geocode` | A table with match quality, a map, inline fixes for addresses that didn't match, and CSV download. |
| **Storm tracker** | `get_tropical_storms` | Active hurricanes, cyclones and typhoons with their past and forecast tracks and wind radii. |

In clients that don't support MCP Apps, every tool still returns a complete text answer.

## Prerequisites

- An Azure Maps account (Gen2).
- A Microsoft Entra app registration for the server:
  - Sign-in audience: your organization only.
  - Application ID URI `api://<client-id>`, and a delegated scope named `access_as_user`.
  - Access token version 2 (`requestedAccessTokenVersion: 2` in the manifest).
- Node.js 20 or later for local work. The server has no npm dependencies.

## Settings

| Setting | Value |
| --- | --- |
| `TENANT_ID` | Your Microsoft Entra tenant ID |
| `CLIENT_ID` | The app registration's client ID |
| `MAPS_CLIENT_ID` | The Azure Maps account's client ID (its unique ID), used as `x-ms-client-id` |
| `RATE_LIMIT_PER_MINUTE` | Optional. Calls per signed-in user per minute. Default 600. Interactive maps load many tiles, so keep this generous. |

The container's system-assigned managed identity needs the **Azure Maps Data Reader** role on the Azure Maps account. The server stores no secrets.

A token is accepted only if it is signed by your tenant, issued by `login.microsoftonline.com/<tenant>/v2.0`, has your app registration as its audience, hasn't expired, comes from your tenant, and has the `access_as_user` scope.

## Endpoints

`POST /mcp` is the MCP endpoint and `GET /health` returns `{"status":"ok"}`. Discovery (`initialize`, `tools/list`, `resources/list`, `resources/read`) is open so clients can read the tool descriptions and the apps. `tools/call` needs a valid access token and returns `401` with a `WWW-Authenticate` header otherwise.

## Deploy to Azure Container Apps

```powershell
az acr create -n <registry> -g <resource-group> -l <region> --sku Basic
az acr build -r <registry> -t azure-maps-mcp:v1 .

az containerapp env create -n <environment> -g <resource-group> -l <region>
az containerapp create -n azure-maps-mcp -g <resource-group> --environment <environment> `
  --image <registry>.azurecr.io/azure-maps-mcp:v1 `
  --registry-server <registry>.azurecr.io --registry-identity system `
  --target-port 8080 --ingress external --min-replicas 1 --cpu 0.5 --memory 1Gi `
  --env-vars TENANT_ID=<tenant-id> CLIENT_ID=<client-id> MAPS_CLIENT_ID=<maps-client-id>

az role assignment create --assignee-object-id <app-identity-principal-id> --assignee-principal-type ServicePrincipal `
  --role "Azure Maps Data Reader" --scope <azure-maps-account-resource-id>
```

The MCP endpoint is the app's URL plus `/mcp`. Keep `--min-replicas 1` so the first call after an idle period doesn't wait for a cold start. The server keeps a small in-memory cache of map tiles, so memory use grows a little under heavy map use; 1 GiB is plenty.

## Run locally

```powershell
$env:MAPS_SUBSCRIPTION_KEY = "<azure maps key>"   # local only; in Azure the managed identity is used
node test/dev-server.js                             # then open http://127.0.0.1:8777/
```

The preview server serves a stand-in MCP Apps host and the `/mcp` endpoint with Microsoft Entra sign-in switched off. Pick a tool, edit its arguments and press **Run** to see its app. Open `http://127.0.0.1:8777/?tool=get_weather&args={"location":{"query":"Seattle"}}` to jump straight to one, and add `&dark=1` for the dark theme. Pages are re-read on every run, so edits to the files in `widgets/` show up when you press **Run** again.

To call one tool from the command line and print its answer:

```powershell
node test/call.js get_route_range '{"origin":{"query":"Seattle Center"},"timeBudgetsMinutes":[10,20]}'
```

To run the server itself locally, also set `TENANT_ID` and `CLIENT_ID` and run `node server.js` (port 8080, or `PORT`).

## Test

`npm test` runs offline tests: the tool catalog, that every app exists and loads nothing from the network, the protocol and sign-in behavior, the Azure Maps client (retries, query building, errors without keys), and tools against canned Azure Maps responses. It needs no network or key.

## How it is built

- `mcp.js`, `server.js`, `auth.js`, `maps-auth.js`: the HTTP server, JSON-RPC handling, Entra token validation and managed identity access.
- `lib/maps.js`: the Azure Maps client. It retries 429 and 5xx responses with backoff, times out after 30 seconds, and allows at most 16 requests to Azure Maps at once (set `MAPS_MAX_CONCURRENT` to change it).
- `lib/tools/`: one file per Azure Maps service. Each tool returns text for the agent and `structuredContent` for its app.
- `widgets/`: the apps. `shared.css`, `bridge.js`, `ui.js` and `map-engine.js` are inserted into every app; the engine draws map tiles and shapes on a canvas.

## Notes

- Anyone in your tenant who can sign in to the app registration and holds the `access_as_user` scope can call the server, and all calls use the server's Azure Maps identity. Restrict who can use the app registration (assignment required on its enterprise application) to limit that.
- Interactive maps request map tiles, which Azure Maps bills as transactions (15 tiles count as one). The server caches base map and satellite tiles for six hours, traffic tiles for a minute and weather tiles for four minutes, and shares one request between callers who ask for the same tile.
- Azure Maps weather along a route forecasts only the next 120 minutes, so `get_weather_along_route` checks the first two hours of longer trips.
