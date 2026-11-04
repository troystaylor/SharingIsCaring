using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights telemetry (hardcoded per repo standard)
    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";
    private const string CONNECTOR_NAME = "AzureMapsRoute";

    private const string MAPS_HOST = "https://atlas.microsoft.com";
    private const string MAPS_API_VERSION = "2025-01-01";
    private const string GEOJSON_MEDIA_TYPE = "application/geo+json";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (Context.OperationId)
        {
            case "InvokeMCP":
                return await HandleMcpAsync().ConfigureAwait(false);
            case "GetRouteDirectionsSimple":
                return await HandleSimpleDirectionsAsync().ConfigureAwait(false);
            case "GetRouteDirections":
            case "GetRouteMatrix":
            case "GetRouteRange":
            case "SnapToRoads":
                return await ForwardGeoJsonAsync().ConfigureAwait(false);
            default:
                return await Context.SendAsync(Context.Request, CancellationToken).ConfigureAwait(false);
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  GeoJSON pass-through (REST operations)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the request to Azure Maps as application/geo+json and returns the
    /// response body as application/json so Power Platform can parse it.
    /// </summary>
    private async Task<HttpResponseMessage> ForwardGeoJsonAsync()
    {
        var request = Context.Request;

        if (request.Content != null)
        {
            var body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            request.Content = CreateGeoJsonContent(body);
        }

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GEOJSON_MEDIA_TYPE));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.5));

        var response = await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
        await NormalizeResponseAsync(response).ConfigureAwait(false);
        return response;
    }

    private async Task<HttpResponseMessage> HandleSimpleDirectionsAsync()
    {
        var input = JObject.Parse(await Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false));
        var geoJsonBody = BuildDirectionsBody(input);

        var builder = new UriBuilder(Context.Request.RequestUri) { Path = "/route/directions" };
        Context.Request.RequestUri = builder.Uri;
        Context.Request.Content = CreateGeoJsonContent(geoJsonBody.ToString(Newtonsoft.Json.Formatting.None));

        return await ForwardGeoJsonAsync().ConfigureAwait(false);
    }

    private static HttpContent CreateGeoJsonContent(string body)
    {
        var content = new StringContent(body ?? string.Empty, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(GEOJSON_MEDIA_TYPE);
        return content;
    }

    /// <summary>Rewrites the geo+json response content type to application/json without altering the payload.</summary>
    private static async Task NormalizeResponseAsync(HttpResponseMessage response)
    {
        if (response.Content == null) return;

        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var json = new StringContent(text ?? string.Empty, Encoding.UTF8);
        json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        response.Content = json;
    }

    // ───────────────────────────────────────────────────────────────
    //  Request builders (shared by the simple operation and MCP tools)
    // ───────────────────────────────────────────────────────────────

    private static JObject PointFeature(double longitude, double latitude, JObject properties = null)
    {
        return new JObject
        {
            ["type"] = "Feature",
            ["geometry"] = new JObject
            {
                ["type"] = "Point",
                ["coordinates"] = new JArray(longitude, latitude)
            },
            ["properties"] = properties ?? new JObject()
        };
    }

    private static JObject WaypointFeature(double longitude, double latitude, int index)
    {
        return PointFeature(longitude, latitude, new JObject { ["pointIndex"] = index, ["pointType"] = "waypoint" });
    }

    private static double RequireNumber(JObject source, string name)
    {
        var token = source[name];
        if (token == null || token.Type == JTokenType.Null)
            throw new ArgumentException("Missing required argument: " + name);
        return token.Value<double>();
    }

    private static void CopyIfPresent(JObject source, JObject target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = source[name];
            if (value != null && value.Type != JTokenType.Null)
                target[name] = value.DeepClone();
        }
    }

    private static JObject BuildDirectionsBody(JObject input)
    {
        var features = new JArray();
        var index = 0;
        features.Add(WaypointFeature(RequireNumber(input, "originLongitude"), RequireNumber(input, "originLatitude"), index++));

        if (input["waypoints"] is JArray stops)
        {
            foreach (var stop in stops.OfType<JObject>())
                features.Add(WaypointFeature(RequireNumber(stop, "longitude"), RequireNumber(stop, "latitude"), index++));
        }

        features.Add(WaypointFeature(RequireNumber(input, "destinationLongitude"), RequireNumber(input, "destinationLatitude"), index));

        var body = new JObject { ["type"] = "FeatureCollection", ["features"] = features };
        CopyIfPresent(input, body, "travelMode", "optimizeRoute", "optimizeWaypointOrder", "departAt", "arriveAt", "avoid", "routeOutputOptions", "maxRouteCount");

        if (body["routeOutputOptions"] == null)
            body["routeOutputOptions"] = new JArray("routeSummary", "routePath");

        return body;
    }

    private static JArray ToPositions(JToken token, string name)
    {
        var positions = new JArray();
        var items = token as JArray;
        if (items == null || items.Count == 0)
            throw new ArgumentException("Missing required argument: " + name);

        foreach (var item in items.OfType<JObject>())
            positions.Add(new JArray(RequireNumber(item, "longitude"), RequireNumber(item, "latitude")));

        return positions;
    }

    private static JObject BuildMatrixBody(JObject input)
    {
        var features = new JArray();
        foreach (var kind in new[] { "origins", "destinations" })
        {
            features.Add(new JObject
            {
                ["type"] = "Feature",
                ["geometry"] = new JObject { ["type"] = "MultiPoint", ["coordinates"] = ToPositions(input[kind], kind) },
                ["properties"] = new JObject { ["pointType"] = kind }
            });
        }

        var body = new JObject { ["type"] = "FeatureCollection", ["features"] = features, ["optimizeRoute"] = "fastest" };
        CopyIfPresent(input, body, "travelMode", "departAt", "arriveAt", "traffic", "avoid");
        return body;
    }

    private static JObject BuildRangeBody(JObject input)
    {
        var properties = new JObject();
        CopyIfPresent(input, properties, "timeBudgetInSec", "distanceBudgetInMeters", "travelMode", "optimizeRoute", "departAt", "avoid");
        if (properties["timeBudgetInSec"] == null && properties["distanceBudgetInMeters"] == null)
            throw new ArgumentException("Provide timeBudgetInSec or distanceBudgetInMeters.");

        return PointFeature(RequireNumber(input, "originLongitude"), RequireNumber(input, "originLatitude"), properties);
    }

    private static JObject BuildSnapBody(JObject input)
    {
        var features = new JArray();
        foreach (var position in ToPositions(input["points"], "points"))
            features.Add(PointFeature(position[0].Value<double>(), position[1].Value<double>()));

        var body = new JObject { ["type"] = "FeatureCollection", ["features"] = features };
        CopyIfPresent(input, body, "travelMode", "interpolate", "includeSpeedLimit");
        return body;
    }

    // ───────────────────────────────────────────────────────────────
    //  Azure Maps call used by MCP tools
    // ───────────────────────────────────────────────────────────────

    private async Task<string> CallMapsAsync(string path, JObject body, string language)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MAPS_HOST + path + "?api-version=" + MAPS_API_VERSION)
        {
            Content = CreateGeoJsonContent(body.ToString(Newtonsoft.Json.Formatting.None))
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GEOJSON_MEDIA_TYPE));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.5));
        if (!string.IsNullOrEmpty(language))
            request.Headers.TryAddWithoutValidation("Accept-Language", language);

        IEnumerable<string> keys;
        if (Context.Request.Headers.TryGetValues("subscription-key", out keys))
            request.Headers.TryAddWithoutValidation("subscription-key", keys.First());

        var response = await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Azure Maps returned " + (int)response.StatusCode + " " + response.ReasonPhrase + ": " + text);

        return text;
    }

    // ───────────────────────────────────────────────────────────────
    //  MCP Protocol Handler
    // ───────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
        await LogToAppInsightsAsync("MCP_Request", new Dictionary<string, string> { ["correlationId"] = correlationId }).ConfigureAwait(false);

        try
        {
            var body = await Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var request = JObject.Parse(body);

            var method = request["method"]?.ToString();
            var requestId = request["id"];
            var @params = request["params"] as JObject ?? new JObject();

            switch (method)
            {
                case "initialize":
                    return HandleInitialize(requestId);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                case "tools/list":
                    return HandleToolsList(requestId);

                case "tools/call":
                    return await HandleToolsCall(@params, requestId, correlationId).ConfigureAwait(false);

                case "resources/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

                case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsightsAsync("MCP_Error", ex, new Dictionary<string, string> { ["correlationId"] = correlationId }).ConfigureAwait(false);
            return CreateJsonRpcErrorResponse(null, -32603, "Internal error: " + ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = "2025-03-26",
            ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = false } },
            ["serverInfo"] = new JObject { ["name"] = "azure-maps-route-mcp", ["version"] = "1.0.0" }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private static readonly string[] TravelModes = { "driving", "truck", "walking" };
    private static readonly string[] OptimizeModes = { "short", "shortest", "fastestWithoutTraffic", "fastestWithTraffic" };
    private static readonly string[] AvoidValues = { "limitedAccessHighways", "tollRoads", "ferries", "tunnels", "borderCrossings", "lowEmissionZones", "unpavedRoads" };

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var lonLat = LonLatSchema();

        var tools = new JArray
        {
            CreateTool("get_route_directions",
                "Get the best route between an origin and destination (with optional stops) as a GeoJSON FeatureCollection. The route line is a MultiLineString feature; waypoints and turn-by-turn maneuvers are Point features. Coordinates are [longitude, latitude].",
                new JObject
                {
                    ["originLongitude"] = JP("number", "Longitude of the origin (-180 to 180)."),
                    ["originLatitude"] = JP("number", "Latitude of the origin (-90 to 90)."),
                    ["destinationLongitude"] = JP("number", "Longitude of the destination (-180 to 180)."),
                    ["destinationLatitude"] = JP("number", "Latitude of the destination (-90 to 90)."),
                    ["waypoints"] = new JObject { ["type"] = "array", ["description"] = "Optional stops between origin and destination, in order.", ["items"] = lonLat },
                    ["travelMode"] = EnumProp("Mode of travel. Default: driving.", TravelModes),
                    ["optimizeRoute"] = EnumProp("Route optimization. Default: fastestWithoutTraffic.", OptimizeModes),
                    ["optimizeWaypointOrder"] = JP("boolean", "Reorder intermediate waypoints to reduce route cost."),
                    ["departAt"] = JP("string", "Departure time, ISO 8601. Cannot be combined with arriveAt."),
                    ["arriveAt"] = JP("string", "Arrival time, ISO 8601. Cannot be combined with departAt."),
                    ["avoid"] = EnumArrayProp("Road features to avoid.", AvoidValues),
                    ["routeOutputOptions"] = EnumArrayProp("Data to return. Default: routeSummary and routePath. Add itinerary for turn-by-turn instructions.", new[] { "routeSummary", "routePath", "itinerary" }),
                    ["maxRouteCount"] = JP("integer", "Maximum number of routes to return, including alternatives."),
                    ["language"] = JP("string", "Language for instructions, for example en-US.")
                },
                new[] { "originLongitude", "originLatitude", "destinationLongitude", "destinationLatitude" }),

            CreateTool("get_route_matrix",
                "Calculate travel time and distance between every origin and every destination. Origins multiplied by destinations must not exceed 2,500, and larger matrices have extra restrictions (see the Azure Maps Route Matrix documentation). Returns a GeoJSON FeatureCollection with a summary per origin and destination pair.",
                new JObject
                {
                    ["origins"] = new JObject { ["type"] = "array", ["description"] = "Origin points.", ["items"] = lonLat },
                    ["destinations"] = new JObject { ["type"] = "array", ["description"] = "Destination points.", ["items"] = lonLat },
                    ["travelMode"] = EnumProp("Mode of travel. Default: driving.", TravelModes),
                    ["traffic"] = EnumProp("Traffic model. Default: historical.", new[] { "historical", "live" }),
                    ["departAt"] = JP("string", "Departure time, ISO 8601."),
                    ["arriveAt"] = JP("string", "Arrival time, ISO 8601."),
                    ["avoid"] = EnumArrayProp("Road features to avoid.", new[] { "tollRoads", "unpavedRoads" })
                },
                new[] { "origins", "destinations" }),

            CreateTool("get_route_range",
                "Get the area reachable from an origin within a time or distance budget (an isochrone), returned as a GeoJSON Polygon feature.",
                new JObject
                {
                    ["originLongitude"] = JP("number", "Longitude of the starting point (-180 to 180)."),
                    ["originLatitude"] = JP("number", "Latitude of the starting point (-90 to 90)."),
                    ["timeBudgetInSec"] = JP("number", "Time budget in seconds. Provide this or distanceBudgetInMeters."),
                    ["distanceBudgetInMeters"] = JP("number", "Distance budget in meters. Provide this or timeBudgetInSec."),
                    ["travelMode"] = EnumProp("Mode of travel. Default: driving.", new[] { "driving", "truck" }),
                    ["optimizeRoute"] = EnumProp("Route optimization. Default: fastestWithoutTraffic.", new[] { "shortest", "fastestWithoutTraffic", "fastestWithTraffic" }),
                    ["departAt"] = JP("string", "Departure time, ISO 8601."),
                    ["avoid"] = EnumArrayProp("Road features to avoid.", AvoidValues)
                },
                new[] { "originLongitude", "originLatitude" }),

            CreateTool("snap_to_roads",
                "Snap GPS points to the road network and return the matched route as a GeoJSON FeatureCollection. Optionally interpolate between points and include speed limits.",
                new JObject
                {
                    ["points"] = new JObject { ["type"] = "array", ["description"] = "GPS points in travel order.", ["items"] = lonLat },
                    ["travelMode"] = EnumProp("Mode of travel. Default: driving.", new[] { "driving", "truck" }),
                    ["interpolate"] = JP("boolean", "Add points along the road between the snapped points."),
                    ["includeSpeedLimit"] = JP("boolean", "Include speed limits in kilometers per hour."),
                    ["language"] = JP("string", "Language for road names, for example en-US.")
                },
                new[] { "points" })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        await LogToAppInsightsAsync("MCP_ToolCall", new Dictionary<string, string>
        {
            ["correlationId"] = correlationId,
            ["tool"] = toolName ?? "unknown"
        }).ConfigureAwait(false);

        try
        {
            var language = arguments["language"]?.ToString();
            string resultText;

            switch (toolName)
            {
                case "get_route_directions":
                    resultText = await CallMapsAsync("/route/directions", BuildDirectionsBody(arguments), language).ConfigureAwait(false);
                    break;
                case "get_route_matrix":
                    resultText = await CallMapsAsync("/route/matrix", BuildMatrixBody(arguments), null).ConfigureAwait(false);
                    break;
                case "get_route_range":
                    resultText = await CallMapsAsync("/route/range", BuildRangeBody(arguments), null).ConfigureAwait(false);
                    break;
                case "snap_to_roads":
                    resultText = await CallMapsAsync("/route/snapToRoads", BuildSnapBody(arguments), language).ConfigureAwait(false);
                    break;
                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Unknown tool", toolName);
            }

            return CreateToolSuccessResult(requestId, resultText);
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsightsAsync("MCP_ToolError", ex, new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["tool"] = toolName ?? "unknown"
            }).ConfigureAwait(false);
            return CreateToolErrorResult(requestId, ex.Message);
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  JSON-RPC and tool schema helpers
    // ───────────────────────────────────────────────────────────────

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (data != null) error["data"] = data;

        var response = new JObject { ["jsonrpc"] = "2.0", ["error"] = error, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateToolSuccessResult(JToken requestId, string text)
    {
        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = false
        });
    }

    private HttpResponseMessage CreateToolErrorResult(JToken requestId, string errorMessage)
    {
        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = errorMessage } },
            ["isError"] = true
        });
    }

    private JObject CreateTool(string name, string description, JObject properties, string[] required)
    {
        var tool = new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = properties }
        };
        if (required != null && required.Length > 0)
            tool["inputSchema"]["required"] = new JArray(required);
        return tool;
    }

    private JObject JP(string type, string description)
    {
        return new JObject { ["type"] = type, ["description"] = description };
    }

    private JObject EnumProp(string description, string[] values)
    {
        return new JObject { ["type"] = "string", ["description"] = description, ["enum"] = new JArray(values) };
    }

    private JObject EnumArrayProp(string description, string[] values)
    {
        return new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = new JObject { ["type"] = "string", ["enum"] = new JArray(values) }
        };
    }

    private JObject LonLatSchema()
    {
        return new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["longitude"] = JP("number", "Longitude (-180 to 180)."),
                ["latitude"] = JP("number", "Latitude (-90 to 90).")
            },
            ["required"] = new JArray("longitude", "latitude")
        };
    }

    // ───────────────────────────────────────────────────────────────
    //  Application Insights telemetry (hardcoded per repo standard)
    // ───────────────────────────────────────────────────────────────

    private bool TelemetryConfigured()
    {
        return APP_INSIGHTS_ENABLED
            && !string.IsNullOrEmpty(APP_INSIGHTS_KEY)
            && APP_INSIGHTS_KEY.IndexOf("INSERT_YOUR", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private async Task SendTelemetryAsync(JObject telemetryData)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT))
        {
            request.Content = new StringContent(
                telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LogToAppInsightsAsync(string eventName, IDictionary<string, string> properties = null)
    {
        if (!TelemetryConfigured()) return;

        try
        {
            var props = properties != null ? JObject.FromObject(properties) : new JObject();
            props["connector"] = CONNECTOR_NAME;

            await SendTelemetryAsync(new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject { ["ver"] = 2, ["name"] = eventName, ["properties"] = props }
                }
            }).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }

    private async Task LogExceptionToAppInsightsAsync(string eventName, Exception ex, IDictionary<string, string> properties = null)
    {
        if (!TelemetryConfigured()) return;

        try
        {
            var props = properties != null ? new Dictionary<string, string>(properties) : new Dictionary<string, string>();
            props["event"] = eventName;
            props["connector"] = CONNECTOR_NAME;

            await SendTelemetryAsync(new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Exception",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "ExceptionData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["exceptions"] = new JArray
                        {
                            new JObject
                            {
                                ["typeName"] = ex.GetType().FullName,
                                ["message"] = ex.Message,
                                ["hasFullStack"] = false
                            }
                        },
                        ["properties"] = JObject.FromObject(props)
                    }
                }
            }).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
