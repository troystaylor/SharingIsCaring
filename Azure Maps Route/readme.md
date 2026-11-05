# Azure Maps Route

Custom connector for the [Azure Maps Route API](https://learn.microsoft.com/rest/api/maps/route) that works with GeoJSON end to end, plus static map images from the [Render API](https://learn.microsoft.com/rest/api/maps/render/get-map-static-image). One deployment gives you:

- **Standard operations** for Power Automate, Power Apps, and Logic Apps.
- **An MCP endpoint** (`InvokeMCP`) for Copilot Studio agents.

Azure Maps Route (version `2025-01-01`) reads and returns `application/geo+json`. Power Platform connectors expect `application/json`, so this connector's script sends your request as GeoJSON and returns the response as JSON with the GeoJSON unchanged. You get a standard FeatureCollection you can pass to a map control, a Parse JSON action, or another service.

A matching Copilot plugin is in [Copilot Plugins/Azure Maps Route](../Copilot%20Plugins/Azure%20Maps%20Route/README.md).

## Operations

| Operation | Azure Maps endpoint | Returns |
| --- | --- | --- |
| Get Route Directions (GeoJSON) | `POST /route/directions` | Route line, summary, and optional turn-by-turn itinerary |
| Get Route (simple inputs) | `POST /route/directions` | Same result, from plain longitude and latitude values |
| Get Route Matrix (GeoJSON) | `POST /route/matrix` | Travel time and distance for every origin and destination pair |
| Get Route Range (GeoJSON) | `POST /route/range` | Polygon reachable within a time or distance budget |
| Snap Points to Roads (GeoJSON) | `POST /route/snapToRoads` | Points matched to the road network, optionally with speed limits |
| Get Static Map Image | `GET /map/static` | PNG or JPEG map image with optional pins, lines, polygons, and traffic |
| Invoke Azure Maps Route MCP | Handled by the connector script | JSON-RPC 2.0 for Copilot Studio |

Use **Get Route (simple inputs)** when you have coordinates in separate fields. Use **Get Route Directions (GeoJSON)** when you already have a GeoJSON FeatureCollection of waypoints, or need vehicle specifications for truck routes.

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

Longitude comes before latitude in pins and paths. Add one `pins` or `path` item per definition. A request supports up to 25 pin definitions and 10 paths, and a path takes up to 100 locations, so simplify long routes before drawing them. The image uses the Azure Maps Render API version `2024-04-01`; the Route operations use `2025-01-01`.

In a flow, the response is the image content, which you can attach to an email or post to Teams. The `get_static_map` MCP tool takes the same `format` option, and also accepts `jpg`.

## MCP tools for Copilot Studio

Add the connector to a Copilot Studio agent as a tool. The MCP endpoint exposes:

| Tool | Purpose |
| --- | --- |
| `get_route_directions` | Route between an origin and destination with optional stops |
| `get_route_matrix` | Travel times and distances between origins and destinations |
| `get_route_range` | Area reachable within a time or distance budget |
| `snap_to_roads` | Match GPS points to roads |
| `get_static_map` | Map image, or a route drawn on a map |

To draw a route, pass the route line coordinates from `get_route_directions` to `get_static_map` as `routeCoordinates`. The map is framed to fit the route, long routes are reduced to 100 points, and pins A and B mark the start and end. `get_static_map` returns the image as MCP image content, so how it displays depends on the client.

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
| Static map returns 400 | Provide either `center` or `bbox`, not both, and don't combine `bbox` with `width` or `height`. Check that pin and path coordinates are longitude then latitude. |
| MCP tool returns "Missing required argument" | Supply the named longitude, latitude, or list argument. |

## Authors

Troy Taylor, troy@troystaylor.com, [github.com/troystaylor](https://github.com/troystaylor)
