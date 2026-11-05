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
    private const string RENDER_API_VERSION = "2024-04-01";
    private const string GEOJSON_MEDIA_TYPE = "application/geo+json";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (Context.OperationId)
        {
            case "InvokeMCP":
                return await HandleMcpAsync().ConfigureAwait(false);
            case "GetRouteDirectionsSimple":
                return await HandleFlatAsync("/route/directions", BuildDirectionsBody).ConfigureAwait(false);
            case "GetRouteMatrixSimple":
                return await HandleFlatAsync("/route/matrix", BuildMatrixBody).ConfigureAwait(false);
            case "GetRouteRangeSimple":
                return await HandleFlatAsync("/route/range", BuildRangeBody).ConfigureAwait(false);
            case "SnapToRoadsSimple":
                return await HandleFlatAsync("/route/snapToRoads", BuildSnapBody).ConfigureAwait(false);
            case "GetStaticMapImage":
                return await ForwardStaticMapAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Handles the flat "simple inputs" operations: builds the GeoJSON request body from plain fields
    /// and forwards it to the real Azure Maps path.
    /// </summary>
    private async Task<HttpResponseMessage> HandleFlatAsync(string upstreamPath, Func<JObject, JObject> build)
    {
        var input = JObject.Parse(await Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false));
        var geoJsonBody = build(input);

        var builder = new UriBuilder(Context.Request.RequestUri) { Path = upstreamPath };
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
    //  Static map image (Render API)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Passes the static map request through and translates the friendly format query parameter
    /// into the Accept header that Azure Maps uses to choose PNG or JPEG. The image is returned unchanged.
    /// </summary>
    private async Task<HttpResponseMessage> ForwardStaticMapAsync()
    {
        var request = Context.Request;
        var uri = request.RequestUri;

        string format = null;
        var kept = new List<string>();
        foreach (var part in uri.Query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("format=", StringComparison.OrdinalIgnoreCase))
                format = Uri.UnescapeDataString(part.Substring(7));
            else if (part.StartsWith("pins=", StringComparison.OrdinalIgnoreCase) || part.StartsWith("path=", StringComparison.OrdinalIgnoreCase))
            {
                // Azure Maps needs one repeated parameter per item, so split the semicolon-separated value.
                var name = part.Substring(0, 4);
                var value = Uri.UnescapeDataString(part.Substring(5).Replace("+", "%20"));
                foreach (var item in value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (item.Trim().Length > 0) kept.Add(name + "=" + Uri.EscapeDataString(item.Trim()));
                }
            }
            else
                kept.Add(part);
        }

        request.RequestUri = new Uri(uri.GetLeftPart(UriPartial.Path) + (kept.Count > 0 ? "?" + string.Join("&", kept) : string.Empty));
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            IsJpeg(format) ? "image/jpeg" : "image/png"));

        return await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
    }

    private static bool IsJpeg(string format)
    {
        return string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase);
    }

    private static void FlattenPositions(JToken token, List<double[]> positions)
    {
        var array = token as JArray;
        if (array == null || array.Count == 0) return;

        if (array[0].Type == JTokenType.Integer || array[0].Type == JTokenType.Float)
        {
            if (array.Count >= 2) positions.Add(new[] { array[0].Value<double>(), array[1].Value<double>() });
            return;
        }

        foreach (var child in array) FlattenPositions(child, positions);
    }

    private static double MercatorY(double latitude)
    {
        var clamped = Math.Max(-85.0, Math.Min(85.0, latitude)) * Math.PI / 180.0;
        return Math.Log(Math.Tan(Math.PI / 4.0 + clamped / 2.0));
    }

    /// <summary>Picks the highest zoom at which the bounds fit the image (Azure Maps uses 512 pixel tiles).</summary>
    private static int FitZoom(double minLon, double minLat, double maxLon, double maxLat, int width, int height)
    {
        const double padding = 1.2;
        var lonSpan = (maxLon - minLon) * padding;
        var ySpan = (MercatorY(maxLat) - MercatorY(minLat)) * padding;
        if (lonSpan <= 0 && ySpan <= 0) return 14;

        var zoom = 20.0;
        if (lonSpan > 0) zoom = Math.Min(zoom, Math.Log(width * 360.0 / (512.0 * lonSpan), 2));
        if (ySpan > 0) zoom = Math.Min(zoom, Math.Log(height * 2.0 * Math.PI / (512.0 * ySpan), 2));
        return (int)Math.Max(0, Math.Min(20, Math.Floor(zoom)));
    }

    private static string Pos(double longitude, double latitude)
    {
        return longitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + " " +
               latitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Builds a Render API query from the tool arguments and returns the image as MCP content.</summary>
    private async Task<HttpResponseMessage> HandleStaticMapToolAsync(JToken requestId, JObject args, string extraText = null)
    {
        var width = args["width"]?.Value<int?>() ?? 512;
        var height = args["height"]?.Value<int?>() ?? 512;
        var pins = new List<string>();
        var paths = new List<string>();
        foreach (var pin in (args["pins"] as JArray ?? new JArray())) pins.Add(pin.ToString());
        foreach (var path in (args["path"] as JArray ?? new JArray())) paths.Add(path.ToString());

        var query = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("api-version", RENDER_API_VERSION)
        };

        string placement;
        var route = new List<double[]>();
        FlattenPositions(args["routeCoordinates"], route);

        var bboxValues = new List<double>();
        if (args["bbox"] is JArray bboxArray)
            bboxValues.AddRange(bboxArray.Select(b => b.Value<double>()));
        else if (args["bbox"] != null)
            foreach (var part in args["bbox"].ToString().Split(','))
            {
                double value;
                if (double.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    bboxValues.Add(value);
            }

        if (route.Count > 0)
        {
            var points = route;
            if (points.Count > 100)
            {
                points = new List<double[]>();
                for (var i = 0; i < 100; i++) points.Add(route[(int)Math.Round(i * (route.Count - 1) / 99.0)]);
            }

            var color = (args["routeColor"]?.ToString() ?? "0078D4").TrimStart('#');
            var lineWidth = Math.Max(1, Math.Min(50, args["routeWidth"]?.Value<int?>() ?? 5));
            paths.Add("lc" + color + "|lw" + lineWidth + "||" + string.Join("|", points.Select(p => Pos(p[0], p[1]))));

            if (args["markEndpoints"] == null || args["markEndpoints"].Value<bool>())
            {
                pins.Add("default|co2E7D32||'A'" + Pos(route[0][0], route[0][1]));
                pins.Add("default|coC62828||'B'" + Pos(route[route.Count - 1][0], route[route.Count - 1][1]));
            }

            if (args["centerLongitude"] == null)
            {
                var minLon = route.Min(p => p[0]); var maxLon = route.Max(p => p[0]);
                var minLat = route.Min(p => p[1]); var maxLat = route.Max(p => p[1]);
                var zoom = args["zoom"]?.Value<int?>() ?? FitZoom(minLon, minLat, maxLon, maxLat, width, height);
                query.Add(new KeyValuePair<string, string>("center", Pos((minLon + maxLon) / 2, (minLat + maxLat) / 2).Replace(' ', ',')));
                query.Add(new KeyValuePair<string, string>("zoom", zoom.ToString()));
            }
        }

        if (route.Count == 0 || args["centerLongitude"] != null)
        {
            if (bboxValues.Count == 4)
            {
                query.Add(new KeyValuePair<string, string>("bbox", string.Join(",", bboxValues.Select(b => b.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
                width = 0; height = 0;
            }
            else
            {
                var lon = RequireNumber(args, "centerLongitude");
                var lat = RequireNumber(args, "centerLatitude");
                query.Add(new KeyValuePair<string, string>("center", Pos(lon, lat).Replace(' ', ',')));
                query.Add(new KeyValuePair<string, string>("zoom", (args["zoom"]?.Value<int?>() ?? 12).ToString()));
            }
        }

        placement = query.First(q => q.Key == "center" || q.Key == "bbox").Value;

        if (width > 0) query.Add(new KeyValuePair<string, string>("width", width.ToString()));
        if (height > 0) query.Add(new KeyValuePair<string, string>("height", height.ToString()));
        foreach (var name in new[] { "tilesetId", "trafficLayer", "language", "view" })
        {
            var value = args[name]?.ToString();
            if (!string.IsNullOrEmpty(value)) query.Add(new KeyValuePair<string, string>(name, value));
        }
        foreach (var pin in pins) query.Add(new KeyValuePair<string, string>("pins", pin));
        foreach (var path in paths) query.Add(new KeyValuePair<string, string>("path", path));

        var url = MAPS_HOST + "/map/static?" + string.Join("&", query.Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value)));
        var jpeg = IsJpeg(args["format"]?.ToString());

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(jpeg ? "image/jpeg" : "image/png"));
        IEnumerable<string> keys;
        if (Context.Request.Headers.TryGetValues("subscription-key", out keys))
            request.Headers.TryAddWithoutValidation("subscription-key", keys.First());

        var response = await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException("Azure Maps returned " + (int)response.StatusCode + " " + response.ReasonPhrase + ": " + error);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var mimeType = response.Content.Headers.ContentType?.MediaType ?? (jpeg ? "image/jpeg" : "image/png");

        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["content"] = new JArray
            {
                new JObject { ["type"] = "image", ["data"] = Convert.ToBase64String(bytes), ["mimeType"] = mimeType },
                new JObject { ["type"] = "text", ["text"] = (extraText != null ? extraText + " " : string.Empty) + "Static map (" + mimeType + ", " + bytes.Length + " bytes), placement " + placement + "." + (extraText == null ? BingCenterHint(placement, query) : string.Empty) }
            },
            ["isError"] = false
        });
    }

    /// <summary>For center based maps, suggests a Bing Maps link so the user can open the same area when the image doesn't display.</summary>
    private static string BingCenterHint(string placement, List<KeyValuePair<string, string>> query)
    {
        var parts = placement.Split(',');
        double longitude, latitude;
        if (parts.Length != 2
            || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out longitude)
            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out latitude))
            return string.Empty;

        var zoom = query.FirstOrDefault(q => q.Key == "zoom").Value ?? "12";
        return " The map image may not display in every client. Share this link so the user can open the area: https://www.bing.com/maps?cp="
            + latitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + "~"
            + longitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + "&lvl=" + zoom;
    }

    /// <summary>Gets a route and draws it on a map in one step, so agents don't have to pass route coordinates between tools.</summary>
    private async Task<HttpResponseMessage> HandleRouteMapToolAsync(JToken requestId, JObject args)
    {
        var directionsArgs = (JObject)args.DeepClone();
        directionsArgs["routeOutputOptions"] = new JArray("routeSummary", "routePath");

        var json = await CallMapsAsync("/route/directions", BuildDirectionsBody(directionsArgs), args["language"]?.ToString()).ConfigureAwait(false);
        var routeFeature = JObject.Parse(json)["features"]?.FirstOrDefault(f => f["properties"]?["type"]?.ToString() == "RoutePath");
        if (routeFeature == null)
            throw new InvalidOperationException("No route was found between those points.");

        var mapArgs = new JObject { ["routeCoordinates"] = routeFeature["geometry"]["coordinates"] };
        CopyIfPresent(args, mapArgs, "routeColor", "routeWidth", "markEndpoints", "width", "height", "zoom", "tilesetId", "trafficLayer", "language", "view", "format");

        var meters = routeFeature["properties"]?["distanceInMeters"]?.Value<double>() ?? 0;
        var seconds = routeFeature["properties"]?["durationInSeconds"]?.Value<double>() ?? 0;
        var summary = "Route: " + (meters / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            + " km, about " + Math.Round(seconds / 60.0) + " minutes. "
            + "The map image may not display in every client. Always share this link so the user can open the route on a map: "
            + BingRouteUrl(args);

        return await HandleStaticMapToolAsync(requestId, mapArgs, summary).ConfigureAwait(false);
    }

    private static string BingPos(double latitude, double longitude)
    {
        return "pos." + latitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
            + "_" + longitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Builds a Bing Maps directions link (origin, stops, destination) that opens in any browser without a key.</summary>
    private static string BingRouteUrl(JObject args)
    {
        var input = NormalizeInput(args);
        var stops = new List<string> { BingPos(RequireNumber(input, "originLatitude"), RequireNumber(input, "originLongitude")) };

        if (input["waypoints"] is JArray via)
            foreach (var stop in via.OfType<JObject>())
                stops.Add(BingPos(RequireNumber(stop, "latitude"), RequireNumber(stop, "longitude")));

        stops.Add(BingPos(RequireNumber(input, "destinationLatitude"), RequireNumber(input, "destinationLongitude")));

        var mode = input["travelMode"]?.ToString() == "walking" ? "W" : "D";
        return "https://www.bing.com/maps?rtp=" + string.Join("~", stops) + "&mode=" + mode;
    }

    /// <summary>
    /// Copilot Studio sends every optional tool input, with null, empty text, or empty lists for the ones the
    /// agent left blank. Drop those so they behave as "not provided".
    /// </summary>
    private static JObject RemoveBlankArguments(JObject args)
    {
        var zeroMeansBlank = new HashSet<string> { "maxRouteCount", "routeWidth", "width", "height", "timeBudgetInSec", "distanceBudgetInMeters" };
        var cleaned = new JObject();

        foreach (var property in args.Properties())
        {
            var value = property.Value;
            if (value == null || value.Type == JTokenType.Null) continue;
            if (value.Type == JTokenType.String && string.IsNullOrWhiteSpace(value.ToString())) continue;
            if (value.Type == JTokenType.Array && !value.HasValues) continue;
            if ((value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
                && zeroMeansBlank.Contains(property.Name) && value.Value<double>() == 0) continue;

            cleaned[property.Name] = value;
        }

        return cleaned;
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

    /// <summary>
    /// Copilot Studio agents can't pass arrays of objects to connector operations, so the simple operations
    /// take text instead: points as "longitude,latitude;longitude,latitude" and lists as comma-separated values.
    /// Real arrays (from MCP clients) are left as they are.
    /// </summary>
    private static JObject NormalizeInput(JObject input)
    {
        var result = (JObject)input.DeepClone();

        foreach (var name in new[] { "waypoints", "origins", "destinations", "points" })
        {
            var text = result[name] as JValue;
            if (text == null || text.Type != JTokenType.String) continue;

            var positions = new JArray();
            foreach (var pair in text.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(',');
                double longitude, latitude;
                if (parts.Length != 2
                    || !double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out longitude)
                    || !double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out latitude))
                    throw new ArgumentException(name + " must be longitude,latitude pairs separated by semicolons, for example -122.33,47.6;-122.12,47.67. Got: " + pair);

                positions.Add(new JObject { ["longitude"] = longitude, ["latitude"] = latitude });
            }
            result[name] = positions;
        }

        foreach (var name in new[] { "avoid", "routeOutputOptions" })
        {
            var text = result[name] as JValue;
            if (text == null || text.Type != JTokenType.String) continue;

            var items = text.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);
            result[name] = new JArray(items);
        }

        return result;
    }

    private static JObject BuildDirectionsBody(JObject input)
    {
        input = NormalizeInput(input);
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
        input = NormalizeInput(input);
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
        input = NormalizeInput(input);
        var properties = new JObject();
        CopyIfPresent(input, properties, "timeBudgetInSec", "distanceBudgetInMeters", "travelMode", "optimizeRoute", "departAt", "avoid");
        if (properties["timeBudgetInSec"] == null && properties["distanceBudgetInMeters"] == null)
            throw new ArgumentException("Provide timeBudgetInSec or distanceBudgetInMeters.");

        return PointFeature(RequireNumber(input, "originLongitude"), RequireNumber(input, "originLatitude"), properties);
    }

    private static JObject BuildSnapBody(JObject input)
    {
        input = NormalizeInput(input);
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
                    ["waypoints"] = JP("string", "Optional stops between origin and destination, in order, as longitude,latitude pairs separated by semicolons, for example -122.2,47.62;-122.15,47.65."),
                    ["travelMode"] = EnumProp("Mode of travel. Default: driving.", TravelModes),
                    ["optimizeRoute"] = EnumProp("Route optimization. Default: fastestWithoutTraffic.", OptimizeModes),
                    ["optimizeWaypointOrder"] = JP("boolean", "Reorder intermediate waypoints to reduce route cost."),
                    ["departAt"] = JP("string", "Departure time, ISO 8601. Cannot be combined with arriveAt."),
                    ["arriveAt"] = JP("string", "Arrival time, ISO 8601. Cannot be combined with departAt."),
                    ["avoid"] = JP("string", "Road features to avoid, comma-separated. Values: " + string.Join(", ", AvoidValues) + "."),
                    ["routeOutputOptions"] = JP("string", "Data to return, comma-separated. Default: routeSummary,routePath. Add itinerary for turn-by-turn instructions."),
                    ["maxRouteCount"] = JP("integer", "Maximum number of routes to return, including alternatives."),
                    ["language"] = JP("string", "Language for instructions, for example en-US.")
                },
                new[] { "originLongitude", "originLatitude", "destinationLongitude", "destinationLatitude" }),

            CreateTool("get_route_matrix",
                "Calculate travel time and distance between every origin and every destination. Origins multiplied by destinations must not exceed 2,500, and larger matrices have extra restrictions (see the Azure Maps Route Matrix documentation). Returns a GeoJSON FeatureCollection with a summary per origin and destination pair.",
                new JObject
                {
                    ["origins"] = JP("string", "Origin points as longitude,latitude pairs separated by semicolons, for example -122.33,47.6;-122.2,47.5."),
                    ["destinations"] = JP("string", "Destination points as longitude,latitude pairs separated by semicolons."),
                    ["travelMode"] = EnumProp("Mode of travel. Default: driving.", TravelModes),
                    ["traffic"] = EnumProp("Traffic model. Default: historical.", new[] { "historical", "live" }),
                    ["departAt"] = JP("string", "Departure time, ISO 8601."),
                    ["arriveAt"] = JP("string", "Arrival time, ISO 8601."),
                    ["avoid"] = JP("string", "Road features to avoid, comma-separated. Values: tollRoads, unpavedRoads.")
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
                    ["avoid"] = JP("string", "Road features to avoid, comma-separated. Values: " + string.Join(", ", AvoidValues) + ".")
                },
                new[] { "originLongitude", "originLatitude" }),

            CreateTool("snap_to_roads",
                "Snap GPS points to the road network and return the matched route as a GeoJSON FeatureCollection. Optionally interpolate between points and include speed limits.",
                new JObject
                {
                    ["points"] = JP("string", "GPS points in travel order as longitude,latitude pairs separated by semicolons, for example -122.335,47.608;-122.332,47.606."),
                    ["travelMode"] = EnumProp("Mode of travel. Default: driving.", new[] { "driving", "truck" }),
                    ["interpolate"] = JP("boolean", "Add points along the road between the snapped points."),
                    ["includeSpeedLimit"] = JP("boolean", "Include speed limits in kilometers per hour."),
                    ["language"] = JP("string", "Language for road names, for example en-US.")
                },
                new[] { "points" }),

            CreateTool("get_route_map",
                "Get driving, truck, or walking directions and draw the route on a map image in one step. Returns the map image plus the distance and travel time. Use this when the user asks to see a route on a map. Coordinates are longitude and latitude.",
                new JObject
                {
                    ["originLongitude"] = JP("number", "Longitude of the origin (-180 to 180)."),
                    ["originLatitude"] = JP("number", "Latitude of the origin (-90 to 90)."),
                    ["destinationLongitude"] = JP("number", "Longitude of the destination (-180 to 180)."),
                    ["destinationLatitude"] = JP("number", "Latitude of the destination (-90 to 90)."),
                    ["waypoints"] = JP("string", "Optional stops between origin and destination, in order, as longitude,latitude pairs separated by semicolons."),
                    ["travelMode"] = EnumProp("Mode of travel. Default: driving.", TravelModes),
                    ["optimizeRoute"] = EnumProp("Route optimization. Default: fastestWithoutTraffic.", OptimizeModes),
                    ["avoid"] = JP("string", "Road features to avoid, comma-separated. Values: " + string.Join(", ", AvoidValues) + "."),
                    ["departAt"] = JP("string", "Departure time, ISO 8601."),
                    ["routeColor"] = JP("string", "Route line color as six hex digits without #. Default: 0078D4."),
                    ["routeWidth"] = JP("integer", "Route line width in pixels, 1 to 50. Default: 5."),
                    ["markEndpoints"] = JP("boolean", "Add pins labeled A and B at the start and end. Default: true."),
                    ["width"] = JP("integer", "Image width in pixels, 80 to 2000. Default: 512."),
                    ["height"] = JP("integer", "Image height in pixels, 80 to 1500. Default: 512."),
                    ["tilesetId"] = EnumProp("Map style. Default: microsoft.base.road.", new[] { "microsoft.base.road", "microsoft.base.darkgrey", "microsoft.imagery" }),
                    ["trafficLayer"] = EnumProp("Show live traffic. Default: none.", new[] { "microsoft.traffic.relative.main", "none" }),
                    ["format"] = EnumProp("Image format. Default: png.", new[] { "png", "jpeg" }),
                    ["language"] = JP("string", "Language for map labels, for example en-US.")
                },
                new[] { "originLongitude", "originLatitude", "destinationLongitude", "destinationLatitude" }),

            CreateTool("get_static_map",
                "Render a static map image (PNG or JPEG) with optional pushpins, lines, polygons, and live traffic. Give a center point and zoom, or a bbox. To show a route, use get_route_map instead.",
                new JObject
                {
                    ["centerLongitude"] = JP("number", "Longitude of the map center (-180 to 180). Required unless bbox is given."),
                    ["centerLatitude"] = JP("number", "Latitude of the map center (-90 to 90)."),
                    ["zoom"] = JP("integer", "Zoom level 0 (world) to 20 (street). Default: 12."),
                    ["bbox"] = JP("string", "Area to show as minLon,minLat,maxLon,maxLat. Image size is then set by the service."),
                    ["width"] = JP("integer", "Image width in pixels, 80 to 2000. Default: 512."),
                    ["height"] = JP("integer", "Image height in pixels, 80 to 1500. Default: 512."),
                    ["tilesetId"] = EnumProp("Map style. Default: microsoft.base.road.", new[] { "microsoft.base.road", "microsoft.base.darkgrey", "microsoft.imagery" }),
                    ["trafficLayer"] = EnumProp("Show live traffic. Default: none.", new[] { "microsoft.traffic.relative.main", "none" }),
                    ["pins"] = new JObject { ["type"] = "array", ["description"] = "Pushpin definitions, for example default|co0078D4||'1'-122.33 47.6. Longitude comes before latitude.", ["items"] = new JObject { ["type"] = "string" } },
                    ["path"] = new JObject { ["type"] = "array", ["description"] = "Line, polygon, or circle definitions, for example lcFF0000|lw3||-122.33 47.6|-122.12 47.67.", ["items"] = new JObject { ["type"] = "string" } },
                    ["language"] = JP("string", "Language for map labels, for example en-US."),
                    ["format"] = EnumProp("Image format. Default: png.", new[] { "png", "jpeg" })
                },
                new string[0])
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = RemoveBlankArguments(@params["arguments"] as JObject ?? new JObject());

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
                    return CreateJsonRpcSuccessResponse(requestId, new JObject
                    {
                        ["content"] = new JArray
                        {
                            new JObject { ["type"] = "text", ["text"] = resultText },
                            new JObject { ["type"] = "text", ["text"] = "To let the user see this route on a map, share this link: " + BingRouteUrl(arguments) }
                        },
                        ["isError"] = false
                    });
                case "get_route_matrix":
                    resultText = await CallMapsAsync("/route/matrix", BuildMatrixBody(arguments), null).ConfigureAwait(false);
                    break;
                case "get_route_range":
                    resultText = await CallMapsAsync("/route/range", BuildRangeBody(arguments), null).ConfigureAwait(false);
                    break;
                case "snap_to_roads":
                    resultText = await CallMapsAsync("/route/snapToRoads", BuildSnapBody(arguments), language).ConfigureAwait(false);
                    break;
                case "get_static_map":
                    return await HandleStaticMapToolAsync(requestId, arguments).ConfigureAwait(false);
                case "get_route_map":
                    return await HandleRouteMapToolAsync(requestId, arguments).ConfigureAwait(false);
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
