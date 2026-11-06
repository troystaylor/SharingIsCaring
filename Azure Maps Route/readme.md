# Azure Maps Route

Custom connector for the [Azure Maps Route API](https://learn.microsoft.com/rest/api/maps/route) that works with GeoJSON end to end, plus static map images from the [Render API](https://learn.microsoft.com/rest/api/maps/render/get-map-static-image). One deployment gives you:

- **Standard operations** for Power Automate, Power Apps, and Logic Apps.
- **An MCP endpoint** (`InvokeMCP`) for Copilot Studio agents.

Azure Maps Route (version `2025-01-01`) reads and returns `application/geo+json`. Power Platform connectors expect `application/json`, so this connector's script sends your request as GeoJSON and returns the response as JSON with the GeoJSON unchanged. You get a standard FeatureCollection you can pass to a map control, a Parse JSON action, or another service.

A matching Copilot plugin is in [Copilot Plugins/Azure Maps Route](../Copilot%20Plugins/Azure%20Maps%20Route/README.md). Plugins connect to an MCP server by URL and can't use a custom connector, so the [hosted MCP server](../Container%20Apps/Azure%20Maps%20Route/README.md) provides the route and map tools as a small server you can deploy to Azure Container Apps.

## Operations

| Operation | Azure Maps endpoint | Returns |
| --- | --- | --- |
| Get Route Directions (GeoJSON) | `POST /route/directions` | Route line, summary, and optional turn-by-turn itinerary |
| Get Route (simple inputs) | `POST /route/directions` | Same result, from plain longitude and latitude values |
| Get Route Matrix (GeoJSON) | `POST /route/matrix` | Travel time and distance for every origin and destination pair |
| Get Route Matrix (simple inputs) | `POST /route/matrix` | Same result, from text lists of points |
| Get Route Range (GeoJSON) | `POST /route/range` | Polygon reachable within a time or distance budget |
| Get Route Range (simple inputs) | `POST /route/range` | Same result, from plain fields |
| Snap Points to Roads (GeoJSON) | `POST /route/snapToRoads` | Points matched to the road network, optionally with speed limits |
| Snap Points to Roads (simple inputs) | `POST /route/snapToRoads` | Same result, from a text list of points |
| Get Static Map Image | `GET /map/static` | PNG or JPEG map image with optional pins, lines, polygons, and traffic |
| Get Routes Map (several routes on one map) | Handled by the connector script | One map with up to 10 independent routes, plus distance, time, and a link for each |
| Invoke Azure Maps Route MCP | Handled by the connector script | JSON-RPC 2.0 for Copilot Studio |

Use the **(GeoJSON)** operations in flows and apps when you already have GeoJSON, or need vehicle specifications for truck routes. Use the **(simple inputs)** operations from Copilot Studio agents: agents can't pass the nested arrays of objects the GeoJSON operations take, and fail with a `ConnectorPowerFxError`. The simple operations return the same GeoJSON.

The simple operations take points as text, `longitude,latitude` pairs separated by semicolons, for example `-122.33,47.6;-122.12,47.67`, and lists such as `avoid` and `routeOutputOptions` as comma-separated values.

Coordinates are always **longitude first, then latitude**.

## Prerequisites

- An [Azure Maps account](https://learn.microsoft.com/azure/azure-maps/quick-demo-map-app#create-an-azure-maps-account) and one of its keys (Azure portal > your Azure Maps account > **Authentication**).
- Power Platform CLI (`pac`) signed in to your environment.

## Setup

1. Optional: to send connector telemetry to Application Insights, edit the constants at the top of `script.csx`: set `APP_INSIGHTS_ENABLED` to `true` and replace `[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]` with your instrumentation key.
2. Deploy the connector:

   ```powershell
   pac connector create `
     --api-definition-file apiDefinition.swagger.json `
     --api-properties-file apiProperties.json `
     --script-file script.csx
   ```

3. In Power Automate, Power Apps, or Copilot Studio, create a connection and paste your Azure Maps key when prompted. The connector sends it in the `subscription-key` header.

## Use in a flow or app

**Get Route (simple inputs)** body:

```json
{
  "originLongitude": -122.335167,
  "originLatitude": 47.608013,
  "destinationLongitude": -122.12151,
  "destinationLatitude": 47.67399,
  "travelMode": "driving",
  "optimizeRoute": "fastestWithTraffic",
  "routeOutputOptions": ["routeSummary", "routePath"]
}
```

**Get Route Directions (GeoJSON)** body:

```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "geometry": { "type": "Point", "coordinates": [-122.335167, 47.608013] },
      "properties": { "pointIndex": 0, "pointType": "waypoint" }
    },
    {
      "type": "Feature",
      "geometry": { "type": "Point", "coordinates": [-122.12151, 47.67399] },
      "properties": { "pointIndex": 1, "pointType": "waypoint" }
    }
  ],
  "travelMode": "driving",
  "routeOutputOptions": ["routeSummary", "routePath"]
}
```

`routeOutputOptions` controls what comes back: `routeSummary` (distance and time), `routePath` (the route line as a MultiLineString), and `itinerary` (turn-by-turn instructions). The default for the Azure Maps API is `itinerary` only, so include `routePath` when you want the line to draw on a map.

The response is a GeoJSON FeatureCollection. Distances are in meters and times are in seconds.

**Get Route Matrix** needs two features in the request: a `MultiPoint` with `pointType` `origins` and a `MultiPoint` with `pointType` `destinations`. Origins multiplied by destinations can't exceed 2,500.

**Get Route Range** takes a single `Point` feature with a `timeBudgetInSec` or `distanceBudgetInMeters` property. Supported travel modes are `driving` and `truck`.

## Static map images

**Get Static Map Image** returns a map as a PNG or JPEG. Give it either a `center` (`longitude,latitude`) and `zoom`, or a `bbox` (`minLon,minLat,maxLon,maxLat`), not both. Do not combine `bbox` with `width` or `height`.

Example query values:

| Parameter | Example |
| --- | --- |
| `center` | `-122.33,47.6` |
| `zoom` | `11` |
| `width`, `height` | `700`, `450` |
| `format` | `png` (default) or `jpeg` |
| `pins` | `default\|co0078D4\|\|'A'-122.335 47.608` |
| `path` | `lcFF0000\|lw4\|\|-122.335 47.608\|-122.12 47.674` |

Longitude comes before latitude in pins and paths. To send more than one pin or path, separate the definitions with a semicolon (`;`) in the `pins` or `path` field. A request supports up to 25 pin definitions and 10 paths, and a path takes up to 100 locations, so simplify long routes before drawing them. The image uses the Azure Maps Render API version `2024-04-01`; the Route operations use `2025-01-01`.

In a flow, the response is the image content, which you can attach to an email or post to Teams. The `get_static_map` MCP tool takes the same `format` option, and also accepts `jpg`.

## Several routes on one map

**Get Routes Map** draws several independent routes, each with its own start and end, on one map. Use it to compare routes, or to show many starts to one destination, or one start to many destinations. For a single route, including one with stops, use `get_route_map`.

Write the routes in one text field, as `origin>destination` pairs separated by semicolons, each point as `longitude,latitude`:

```
-122.335167,47.608013>-122.12151,47.67399;-122.30,47.55>-122.20,47.62
```

For many starts to one destination, repeat the destination in every pair. You can have up to 10 routes. `travelMode`, `optimizeRoute`, `avoid`, and `departAt` apply to every route.

What you get:
- **Operation:** a JSON response with `imageBase64` (decode it with `base64ToBinary` in a flow), `imageContentType`, and a `routes` list with each route's `distanceInMeters`, `durationInSeconds`, `bingMapsUrl`, and `drawn`. The image is a JPEG by default; set `format` to `png` if you need one.
- **MCP tool:** `get_routes_map` returns the image as MCP image content, with a text summary that lists each route and its Bing Maps link.
- **The map:** each route is a different color, numbered in the order you wrote them. Pins are labeled `A1`, `B1`, and so on by route number. Where several routes share a start or end, one neutral `A` or `B` pin marks it.
- **Partial results:** a route that can't be found is left off the map and reported in the response. The other routes are still drawn. If none can be found, the call fails.

The map request carries every route line in its URL, and Azure Maps rejects requests over about 8,000 characters. The connector therefore reduces each route line to the points that matter most for its shape, about 100 points each for 1 to 3 routes and fewer as you add more (about 20 each at 10 routes). The response reports the limit in `pointsPerRoute`. Lines stay on the routes at regional zoom but can cut corners on dense city streets. For close-up detail, draw fewer routes per map.

The routes are separate Azure Maps requests, so a map of 10 routes uses 10 route requests and one map image request.

## MCP tools for Copilot Studio

Add the connector to a Copilot Studio agent as a tool. The MCP endpoint exposes:

| Tool | Purpose |
| --- | --- |
| `get_route_directions` | Route between an origin and destination with optional stops |
| `get_route_matrix` | Travel times and distances between origins and destinations |
| `get_route_range` | Area reachable within a time or distance budget |
| `snap_to_roads` | Match GPS points to roads |
| `get_static_map` | Map image for a center point and zoom, or a bounding box |
| `get_route_map` | Directions drawn on a map image in one step, with distance and travel time |
| `get_routes_map` | Several independent routes drawn on one map, each in its own color |

To show a route on a map, use `get_route_map`. It gets the route and draws it, so the agent doesn't have to pass route coordinates between tools. The map is framed to fit the route, long routes are reduced to 100 points, and pins A and B mark the start and end. To show several separate routes on one map, use `get_routes_map`. The map tools return the image as MCP image content, so how it displays depends on the client. A large PNG can be several hundred KB; set `format` to `jpeg` for a smaller image.

The MCP tools take only flat inputs, so Copilot Studio agents can fill them in. Points are text, `longitude,latitude` pairs separated by semicolons, and lists such as `avoid` are comma-separated. Inputs an agent leaves blank are ignored.

Not every client displays MCP image content. For that reason `get_route_map`, `get_routes_map`, and `get_route_directions` also return a Bing Maps link that opens the route (origin, stops, and destination) in any browser, one per route for `get_routes_map`, and `get_static_map` returns one that opens the same area. The tool text tells the agent to share the link. The link needs no key or extra resource.

The tools take plain longitude and latitude values and don't look up addresses. Give your agent coordinates, or add a geocoding tool alongside this one.

## Limits and behavior

- Route directions supports up to 150 waypoints, with up to 10 via waypoints between any two.
- Route matrix supports up to 2,500 origin and destination pairs per call. Matrices over 100 pairs have extra restrictions; see the [Route Matrix documentation](https://learn.microsoft.com/rest/api/maps/route/post-route-matrix?view=rest-maps-2025-01-01).
- Route range supports `driving` and `truck`, not `walking`.
- Batch and asynchronous Route API operations aren't included.
- Static maps support PNG and JPEG, up to 2000 by 1500 pixels.
- Routing availability varies by country or region. See [Azure Maps routing coverage](https://learn.microsoft.com/azure/azure-maps/routing-coverage).

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| 401 Unauthorized | The key in your connection is missing, wrong, or from a different Azure Maps account. Update the connection with a current key. |
| 400 Bad Request on directions | Check that coordinates are longitude then latitude, that `departAt` and `arriveAt` aren't both set, and that at least an origin and destination are present. |
| 400 Bad Request on matrix | Origins multiplied by destinations is over 2,500, a larger matrix doesn't meet the extra restrictions in the Route Matrix documentation, or a feature is missing its `pointType`. |
| Response has no route line | Add `routePath` to `routeOutputOptions`. |
| Copilot Studio agent fails with `ConnectorPowerFxError` ("Not supported type ... RecordsOnlyTableValue") | The agent called a **(GeoJSON)** operation, which takes nested arrays of objects. Use the **(simple inputs)** operation, or turn off the GeoJSON operations for that agent. |
| Static map returns 400 | Provide either `center` or `bbox`, not both, and don't combine `bbox` with `width` or `height`. Check that pin and path coordinates are longitude then latitude. |
| MCP tool returns "Missing required argument" | Supply the named longitude, latitude, or list argument. |
| Get Routes Map returns "Each route must be origin>destination" | Write each route as `longitude,latitude>longitude,latitude` and separate routes with semicolons, for example `-122.33,47.6>-122.12,47.67;-122.3,47.55>-122.2,47.62`. |
| Get Routes Map returns "A map can show at most 10 routes" | Split the routes across several maps. |
| A route is missing from the Get Routes Map image | Check `routes` in the response: `drawn` is `false` and `error` says why, for example that no road route exists between the two points. |

## Authors

Troy Taylor, troy@troystaylor.com, [github.com/troystaylor](https://github.com/troystaylor)
