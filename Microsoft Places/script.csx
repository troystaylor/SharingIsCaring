using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights connection string (leave empty to disable telemetry)
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly string SERVER_NAME = "microsoft-places-mcp";
    private static readonly string SERVER_VERSION = "1.0.0";
    private static readonly string DEFAULT_PROTOCOL_VERSION = "2025-03-26";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            await LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = operationId });

            HttpResponseMessage response;

            switch (operationId)
            {
                case "MCP":
                    response = await HandleMCPRequestAsync(correlationId).ConfigureAwait(false);
                    break;
                case "ListBuildings":
                    response = await HandleListPlacesAsync("building", correlationId).ConfigureAwait(false);
                    break;
                case "ListFloors":
                    response = await HandleListPlacesAsync("floor", correlationId).ConfigureAwait(false);
                    break;
                case "ListSections":
                    response = await HandleListPlacesAsync("section", correlationId).ConfigureAwait(false);
                    break;
                case "ListRooms":
                    response = await HandleListPlacesAsync("room", correlationId).ConfigureAwait(false);
                    break;
                case "ListWorkspaces":
                    response = await HandleListPlacesAsync("workspace", correlationId).ConfigureAwait(false);
                    break;
                case "ListRoomLists":
                    response = await HandleListPlacesAsync("roomlist", correlationId).ConfigureAwait(false);
                    break;
                case "ListDesks":
                    response = await HandleListPlacesAsync("desk", correlationId).ConfigureAwait(false);
                    break;
                case "GetPlace":
                    response = await HandleGetPlaceAsync(correlationId).ConfigureAwait(false);
                    break;
                case "CreatePlace":
                    response = await HandleCreatePlaceAsync(correlationId).ConfigureAwait(false);
                    break;
                case "UpdatePlace":
                    response = await HandleUpdatePlaceAsync(correlationId).ConfigureAwait(false);
                    break;
                case "DeletePlace":
                    response = await HandleDeletePlaceAsync(correlationId).ConfigureAwait(false);
                    break;
                case "ListRoomsInRoomList":
                    response = await HandleListInRoomListAsync("rooms", correlationId).ConfigureAwait(false);
                    break;
                case "ListWorkspacesInRoomList":
                    response = await HandleListInRoomListAsync("workspaces", correlationId).ConfigureAwait(false);
                    break;
                default:
                    response = await ForwardToGraphAsync().ConfigureAwait(false);
                    break;
            }

            var duration = DateTime.UtcNow - startTime;
            await LogToAppInsights("RequestCompleted", new { CorrelationId = correlationId, OperationId = operationId, DurationMs = duration.TotalMilliseconds, StatusCode = (int)response.StatusCode });

            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new { CorrelationId = correlationId, OperationId = operationId, ErrorMessage = ex.Message, ErrorType = ex.GetType().Name });
            throw;
        }
    }

    #region REST Operation Handlers

    private async Task<HttpResponseMessage> HandleListPlacesAsync(string placeType, string correlationId)
    {
        var query = this.Context.Request.RequestUri.Query;
        var path = $"/places/microsoft.graph.{placeType}{query}";
        return await CallGraphAsync("GET", path).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetPlaceAsync(string correlationId)
    {
        var placeId = ExtractPathParameter("placeId");
        var query = this.Context.Request.RequestUri.Query;
        var path = $"/places/{Uri.EscapeDataString(placeId)}{query}";
        return await CallGraphAsync("GET", path).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleCreatePlaceAsync(string correlationId)
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        return await CallGraphAsync("POST", "/places", body).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleUpdatePlaceAsync(string correlationId)
    {
        var placeId = ExtractPathParameter("placeId");
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var path = $"/places/{Uri.EscapeDataString(placeId)}";
        return await CallGraphAsync("PATCH", path, body).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleDeletePlaceAsync(string correlationId)
    {
        var placeId = ExtractPathParameter("placeId");
        var path = $"/places/{Uri.EscapeDataString(placeId)}";
        return await CallGraphAsync("DELETE", path).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleListInRoomListAsync(string type, string correlationId)
    {
        var roomListEmail = ExtractPathParameter("roomListEmail");
        var query = this.Context.Request.RequestUri.Query;
        var path = $"/places/{Uri.EscapeDataString(roomListEmail)}/microsoft.graph.roomlist/{type}{query}";
        return await CallGraphAsync("GET", path).ConfigureAwait(false);
    }

    private string ExtractPathParameter(string paramName)
    {
        var path = this.Context.Request.RequestUri.AbsolutePath;
        var segments = path.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();

        // For paths like /places/{placeId} or /places/{email}/microsoft.graph.roomlist/rooms
        if (paramName == "placeId" && segments.Length >= 2)
        {
            return segments[1];
        }
        if (paramName == "roomListEmail" && segments.Length >= 2)
        {
            return segments[1];
        }

        return null;
    }

    private async Task<HttpResponseMessage> ForwardToGraphAsync()
    {
        var path = this.Context.Request.RequestUri.AbsolutePath;
        var query = this.Context.Request.RequestUri.Query;
        var method = this.Context.Request.Method.Method;

        string body = null;
        if (method == "POST" || method == "PATCH" || method == "PUT")
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        return await CallGraphAsync(method, $"{path}{query}", body).ConfigureAwait(false);
    }

    #endregion

    #region MCP Protocol Handlers

    private async Task<HttpResponseMessage> HandleMCPRequestAsync(string correlationId)
    {
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var request = JObject.Parse(body);
            if (!request.ContainsKey("jsonrpc")) request["jsonrpc"] = "2.0";

            var method = request["method"]?.ToString();
            var id = request["id"];
            var @params = request["params"] as JObject;

            await LogToAppInsights("MCPMethod", new { CorrelationId = correlationId, Method = method });

            switch (method)
            {
                case "initialize":
                    return HandleInitialize(@params, id);
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    return CreateMCPSuccess(new JObject(), id);
                case "tools/list":
                    return HandleToolsList(id);
                case "tools/call":
                    return await HandleToolsCallAsync(@params, id, correlationId).ConfigureAwait(false);
                default:
                    await LogToAppInsights("MCPUnknownMethod", new { CorrelationId = correlationId, Method = method ?? "null" });
                    return CreateMCPError(id, -32601, "Method not found", method ?? "");
            }
        }
        catch (JsonException ex)
        {
            await LogToAppInsights("MCPParseError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateMCPError(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateMCPError(null, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(JObject @params, JToken id)
    {
        var protocolVersion = @params?["protocolVersion"]?.ToString() ?? DEFAULT_PROTOCOL_VERSION;
        var result = new JObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION,
                ["title"] = "Microsoft Places MCP",
                ["description"] = "Manage physical spaces in your organization - buildings, floors, sections, rooms, workspaces, and desks via Microsoft Graph Places API"
            }
        };
        return CreateMCPSuccess(result, id);
    }

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        return CreateMCPSuccess(new JObject { ["tools"] = GetToolDefinitions() }, id);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject @params, JToken id, string correlationId)
    {
        var toolName = @params?["name"]?.ToString();
        var args = @params?["arguments"] as JObject ?? new JObject();
        if (string.IsNullOrWhiteSpace(toolName))
            return CreateMCPError(id, -32602, "Tool name required", "name parameter is required");

        var toolStartTime = DateTime.UtcNow;
        try
        {
            await LogToAppInsights("ToolExecuting", new { CorrelationId = correlationId, Tool = toolName });
            var result = await ExecuteToolAsync(toolName, args, id).ConfigureAwait(false);
            var toolDuration = DateTime.UtcNow - toolStartTime;
            await LogToAppInsights("ToolExecuted", new { CorrelationId = correlationId, Tool = toolName, DurationMs = toolDuration.TotalMilliseconds, Success = true });
            return result;
        }
        catch (ArgumentException ex)
        {
            await LogToAppInsights("ToolError", new { CorrelationId = correlationId, Tool = toolName, Error = ex.Message, ErrorType = "ArgumentException" });
            return CreateToolResult(ex.Message, true, id);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("ToolError", new { CorrelationId = correlationId, Tool = toolName, Error = ex.Message, ErrorType = ex.GetType().Name });
            return CreateToolResult($"Tool error: {ex.Message}", true, id);
        }
    }

    #endregion

    #region Tool Definitions

    private JArray GetToolDefinitions()
    {
        return new JArray
        {
            // === LIST TOOLS ===
            CreateToolDef("list_buildings", "List all buildings in the organization. Buildings are physical structures with addresses and coordinates.",
                new JObject
                {
                    ["top"] = IntProp("Maximum number of buildings to return"),
                    ["skip"] = IntProp("Number of buildings to skip for pagination"),
                    ["select"] = StrProp("Properties to include (comma-separated)")
                }),

            CreateToolDef("list_floors", "List all floors in the organization. Floors belong to buildings and contain sections, rooms, and workspaces.",
                new JObject
                {
                    ["top"] = IntProp("Maximum number of floors to return"),
                    ["skip"] = IntProp("Number of floors to skip for pagination"),
                    ["filter"] = StrProp("OData filter (e.g., parentId eq 'building-id')"),
                    ["select"] = StrProp("Properties to include (comma-separated)")
                }),

            CreateToolDef("list_sections", "List all sections (neighborhoods/zones) in the organization. Sections belong to floors and contain desks and workspaces.",
                new JObject
                {
                    ["top"] = IntProp("Maximum number of sections to return"),
                    ["skip"] = IntProp("Number of sections to skip for pagination"),
                    ["filter"] = StrProp("OData filter (e.g., parentId eq 'floor-id')"),
                    ["select"] = StrProp("Properties to include (comma-separated)")
                }),

            CreateToolDef("list_rooms", "List all meeting rooms in the organization. Rooms have email addresses, capacity, and A/V equipment info.",
                new JObject
                {
                    ["top"] = IntProp("Maximum number of rooms to return"),
                    ["skip"] = IntProp("Number of rooms to skip for pagination"),
                    ["filter"] = StrProp("OData filter (e.g., capacity ge 10)"),
                    ["select"] = StrProp("Properties to include (comma-separated)")
                }),

            CreateToolDef("list_workspaces", "List all workspaces (desk areas) in the organization. Workspaces contain collections of desks with booking modes.",
                new JObject
                {
                    ["top"] = IntProp("Maximum number of workspaces to return"),
                    ["skip"] = IntProp("Number of workspaces to skip for pagination"),
                    ["filter"] = StrProp("OData filter expression"),
                    ["select"] = StrProp("Properties to include (comma-separated)")
                }),

            CreateToolDef("list_desks", "List all individual desks in the organization. Desks can be reservable, drop-in, assigned, or unavailable.",
                new JObject
                {
                    ["top"] = IntProp("Maximum number of desks to return"),
                    ["skip"] = IntProp("Number of desks to skip for pagination"),
                    ["select"] = StrProp("Properties to include (comma-separated)")
                }),

            CreateToolDef("list_room_lists", "List all room lists in the organization. Room lists group rooms and workspaces for the Room Finder.",
                new JObject
                {
                    ["top"] = IntProp("Maximum number of room lists to return"),
                    ["skip"] = IntProp("Number of room lists to skip for pagination"),
                    ["select"] = StrProp("Properties to include (comma-separated)")
                }),

            CreateToolDef("list_rooms_in_room_list", "List all rooms in a specific room list. Use the room list's email address.",
                new JObject
                {
                    ["roomListEmail"] = StrProp("Email address of the room list (e.g., bldg1@contoso.com)", true),
                    ["top"] = IntProp("Maximum number of rooms to return"),
                    ["skip"] = IntProp("Number of rooms to skip")
                }, new[] { "roomListEmail" }),

            CreateToolDef("list_workspaces_in_room_list", "List all workspaces in a specific room list. Use the room list's email address.",
                new JObject
                {
                    ["roomListEmail"] = StrProp("Email address of the room list", true),
                    ["top"] = IntProp("Maximum number of workspaces to return"),
                    ["skip"] = IntProp("Number of workspaces to skip")
                }, new[] { "roomListEmail" }),

            // === GET TOOL ===
            CreateToolDef("get_place", "Get details of a specific place by ID or email address. Returns the full place object with all properties.",
                new JObject
                {
                    ["placeId"] = StrProp("The unique ID or email address of the place", true),
                    ["select"] = StrProp("Properties to include (comma-separated)")
                }, new[] { "placeId" }),

            // === CREATE TOOL ===
            CreateToolDef("create_place", "Create a new place (building, floor, section, desk, room, or workspace). Requires Place.ReadWrite.All permission.",
                new JObject
                {
                    ["type"] = StrProp("Type of place: building, floor, section, desk, room, or workspace", true),
                    ["displayName"] = StrProp("Name of the place", true),
                    ["parentId"] = StrProp("ID of parent place. Required for floor (building ID), section (floor ID), desk/workspace (section ID), room (floor or section ID)"),
                    ["label"] = StrProp("User-defined description"),
                    ["tags"] = ArrProp("Array of custom tags for categorization"),
                    ["isWheelChairAccessible"] = BoolProp("Whether the place is wheelchair accessible"),
                    ["sortOrder"] = IntProp("Sort order for floors (0 = first)"),
                    ["capacity"] = IntProp("Capacity for rooms/workspaces"),
                    ["bookingType"] = StrProp("Booking type for rooms: standard or reserved"),
                    ["mode"] = StrProp("Mode for desks/workspaces: reservable, dropIn, assigned, or unavailable")
                }, new[] { "type", "displayName" }),

            // === UPDATE TOOL ===
            CreateToolDef("update_place", "Update properties of an existing place. Requires Place.ReadWrite.All permission. Cannot update id, placeId, emailAddress, displayName, or bookingType.",
                new JObject
                {
                    ["placeId"] = StrProp("The unique ID of the place to update", true),
                    ["type"] = StrProp("Type of place: building, floor, section, desk, room, workspace, or roomList", true),
                    ["parentId"] = StrProp("New parent ID (to move the place)"),
                    ["label"] = StrProp("New description"),
                    ["tags"] = ArrProp("New tags array"),
                    ["isWheelChairAccessible"] = BoolProp("Update wheelchair accessibility"),
                    ["sortOrder"] = IntProp("New sort order for floors"),
                    ["capacity"] = IntProp("New capacity for rooms/workspaces"),
                    ["nickname"] = StrProp("New nickname for rooms/workspaces"),
                    ["mode"] = StrProp("New mode for desks/workspaces: reservable, dropIn, assigned, or unavailable"),
                    ["phone"] = StrProp("Phone number"),
                    ["street"] = StrProp("Street address"),
                    ["city"] = StrProp("City"),
                    ["state"] = StrProp("State/Province"),
                    ["postalCode"] = StrProp("Postal code"),
                    ["countryOrRegion"] = StrProp("Country or region")
                }, new[] { "placeId", "type" }),

            // === DELETE TOOL ===
            CreateToolDef("delete_place", "Delete a place. Only buildings, floors, sections, and desks can be deleted. Rooms, workspaces, and room lists cannot be deleted.",
                new JObject
                {
                    ["placeId"] = StrProp("The unique ID of the place to delete", true)
                }, new[] { "placeId" })
        };
    }

    private JObject CreateToolDef(string name, string desc, JObject props, string[] required = null)
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = props };
        if (required != null && required.Length > 0) schema["required"] = new JArray(required);
        return new JObject { ["name"] = name, ["description"] = desc, ["inputSchema"] = schema };
    }

    private JObject StrProp(string desc, bool req = false) => new JObject { ["type"] = "string", ["description"] = desc };
    private JObject IntProp(string desc) => new JObject { ["type"] = "integer", ["description"] = desc };
    private JObject BoolProp(string desc) => new JObject { ["type"] = "boolean", ["description"] = desc };
    private JObject ArrProp(string desc, bool req = false) => new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = desc };

    #endregion

    #region Tool Execution

    private async Task<HttpResponseMessage> ExecuteToolAsync(string toolName, JObject args, JToken id)
    {
        switch (toolName)
        {
            // List tools
            case "list_buildings":
                return await ExecuteListAsync("building", args, id).ConfigureAwait(false);
            case "list_floors":
                return await ExecuteListAsync("floor", args, id).ConfigureAwait(false);
            case "list_sections":
                return await ExecuteListAsync("section", args, id).ConfigureAwait(false);
            case "list_rooms":
                return await ExecuteListAsync("room", args, id).ConfigureAwait(false);
            case "list_workspaces":
                return await ExecuteListAsync("workspace", args, id).ConfigureAwait(false);
            case "list_desks":
                return await ExecuteListAsync("desk", args, id).ConfigureAwait(false);
            case "list_room_lists":
                return await ExecuteListAsync("roomlist", args, id).ConfigureAwait(false);
            case "list_rooms_in_room_list":
                return await ExecuteListInRoomListAsync("rooms", args, id).ConfigureAwait(false);
            case "list_workspaces_in_room_list":
                return await ExecuteListInRoomListAsync("workspaces", args, id).ConfigureAwait(false);

            // Get tool
            case "get_place":
                return await ExecuteGetPlaceAsync(args, id).ConfigureAwait(false);

            // Create tool
            case "create_place":
                return await ExecuteCreatePlaceAsync(args, id).ConfigureAwait(false);

            // Update tool
            case "update_place":
                return await ExecuteUpdatePlaceAsync(args, id).ConfigureAwait(false);

            // Delete tool
            case "delete_place":
                return await ExecuteDeletePlaceAsync(args, id).ConfigureAwait(false);

            default:
                return CreateMCPError(id, -32601, "Unknown tool", toolName);
        }
    }

    private async Task<HttpResponseMessage> ExecuteListAsync(string placeType, JObject args, JToken id)
    {
        var queryParts = new List<string>();

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var skip = ArgOpt(args, "skip");
        if (!string.IsNullOrEmpty(skip)) queryParts.Add($"$skip={skip}");

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var filter = ArgOpt(args, "filter");
        if (!string.IsNullOrEmpty(filter)) queryParts.Add($"$filter={Uri.EscapeDataString(filter)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        var path = $"/places/microsoft.graph.{placeType}{query}";

        return await CallGraphForToolAsync("GET", path, null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteListInRoomListAsync(string type, JObject args, JToken id)
    {
        var roomListEmail = Arg(args, "roomListEmail");
        var queryParts = new List<string>();

        var top = ArgOpt(args, "top");
        if (!string.IsNullOrEmpty(top)) queryParts.Add($"$top={top}");

        var skip = ArgOpt(args, "skip");
        if (!string.IsNullOrEmpty(skip)) queryParts.Add($"$skip={skip}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        var path = $"/places/{Uri.EscapeDataString(roomListEmail)}/microsoft.graph.roomlist/{type}{query}";

        return await CallGraphForToolAsync("GET", path, null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteGetPlaceAsync(JObject args, JToken id)
    {
        var placeId = Arg(args, "placeId");
        var queryParts = new List<string>();

        var select = ArgOpt(args, "select");
        if (!string.IsNullOrEmpty(select)) queryParts.Add($"$select={Uri.EscapeDataString(select)}");

        var query = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
        var path = $"/places/{Uri.EscapeDataString(placeId)}{query}";

        return await CallGraphForToolAsync("GET", path, null, id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteCreatePlaceAsync(JObject args, JToken id)
    {
        var placeType = Arg(args, "type").ToLowerInvariant();
        var displayName = Arg(args, "displayName");

        var body = new JObject
        {
            ["@odata.type"] = $"microsoft.graph.{placeType}",
            ["displayName"] = displayName
        };

        // Add parentId if provided
        var parentId = ArgOpt(args, "parentId");
        if (!string.IsNullOrEmpty(parentId)) body["parentId"] = parentId;

        // Add optional properties
        var label = ArgOpt(args, "label");
        if (!string.IsNullOrEmpty(label)) body["label"] = label;

        var tagsArr = args["tags"] as JArray;
        if (tagsArr != null && tagsArr.Count > 0) body["tags"] = tagsArr;

        var accessible = args["isWheelChairAccessible"];
        if (accessible != null) body["isWheelChairAccessible"] = accessible;

        var sortOrder = ArgOpt(args, "sortOrder");
        if (!string.IsNullOrEmpty(sortOrder)) body["sortOrder"] = int.Parse(sortOrder);

        var capacity = ArgOpt(args, "capacity");
        if (!string.IsNullOrEmpty(capacity)) body["capacity"] = int.Parse(capacity);

        var bookingType = ArgOpt(args, "bookingType");
        if (!string.IsNullOrEmpty(bookingType)) body["bookingType"] = bookingType;

        // Handle mode for desks/workspaces
        var mode = ArgOpt(args, "mode");
        if (!string.IsNullOrEmpty(mode))
        {
            var modeType = mode.ToLowerInvariant() switch
            {
                "reservable" => "microsoft.graph.reservablePlaceMode",
                "dropin" => "microsoft.graph.dropInPlaceMode",
                "assigned" => "microsoft.graph.assignedPlaceMode",
                "unavailable" => "microsoft.graph.unavailablePlaceMode",
                _ => $"microsoft.graph.{mode}PlaceMode"
            };
            body["mode"] = new JObject { ["@odata.type"] = modeType };
        }

        return await CallGraphForToolAsync("POST", "/places", body.ToString(Newtonsoft.Json.Formatting.None), id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteUpdatePlaceAsync(JObject args, JToken id)
    {
        var placeId = Arg(args, "placeId");
        var placeType = Arg(args, "type").ToLowerInvariant();

        var body = new JObject
        {
            ["@odata.type"] = $"microsoft.graph.{placeType}"
        };

        // Add optional update properties
        var parentId = ArgOpt(args, "parentId");
        if (!string.IsNullOrEmpty(parentId)) body["parentId"] = parentId;

        var label = ArgOpt(args, "label");
        if (!string.IsNullOrEmpty(label)) body["label"] = label;

        var tagsArr = args["tags"] as JArray;
        if (tagsArr != null) body["tags"] = tagsArr;

        var accessible = args["isWheelChairAccessible"];
        if (accessible != null) body["isWheelChairAccessible"] = accessible;

        var sortOrder = ArgOpt(args, "sortOrder");
        if (!string.IsNullOrEmpty(sortOrder)) body["sortOrder"] = int.Parse(sortOrder);

        var capacity = ArgOpt(args, "capacity");
        if (!string.IsNullOrEmpty(capacity)) body["capacity"] = int.Parse(capacity);

        var nickname = ArgOpt(args, "nickname");
        if (!string.IsNullOrEmpty(nickname)) body["nickname"] = nickname;

        var phone = ArgOpt(args, "phone");
        if (!string.IsNullOrEmpty(phone)) body["phone"] = phone;

        // Handle address fields
        var street = ArgOpt(args, "street");
        var city = ArgOpt(args, "city");
        var state = ArgOpt(args, "state");
        var postalCode = ArgOpt(args, "postalCode");
        var country = ArgOpt(args, "countryOrRegion");

        if (!string.IsNullOrEmpty(street) || !string.IsNullOrEmpty(city) || !string.IsNullOrEmpty(state) || !string.IsNullOrEmpty(postalCode) || !string.IsNullOrEmpty(country))
        {
            var address = new JObject();
            if (!string.IsNullOrEmpty(street)) address["street"] = street;
            if (!string.IsNullOrEmpty(city)) address["city"] = city;
            if (!string.IsNullOrEmpty(state)) address["state"] = state;
            if (!string.IsNullOrEmpty(postalCode)) address["postalCode"] = postalCode;
            if (!string.IsNullOrEmpty(country)) address["countryOrRegion"] = country;
            body["address"] = address;
        }

        // Handle mode for desks/workspaces
        var mode = ArgOpt(args, "mode");
        if (!string.IsNullOrEmpty(mode))
        {
            var modeType = mode.ToLowerInvariant() switch
            {
                "reservable" => "microsoft.graph.reservablePlaceMode",
                "dropin" => "microsoft.graph.dropInPlaceMode",
                "assigned" => "microsoft.graph.assignedPlaceMode",
                "unavailable" => "microsoft.graph.unavailablePlaceMode",
                _ => $"microsoft.graph.{mode}PlaceMode"
            };
            body["mode"] = new JObject { ["@odata.type"] = modeType };
        }

        var path = $"/places/{Uri.EscapeDataString(placeId)}";
        return await CallGraphForToolAsync("PATCH", path, body.ToString(Newtonsoft.Json.Formatting.None), id).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ExecuteDeletePlaceAsync(JObject args, JToken id)
    {
        var placeId = Arg(args, "placeId");
        var path = $"/places/{Uri.EscapeDataString(placeId)}";
        return await CallGraphForToolAsync("DELETE", path, null, id).ConfigureAwait(false);
    }

    #endregion

    #region Graph API Helpers

    private string Arg(JObject args, string key)
    {
        var val = args?[key]?.ToString();
        if (string.IsNullOrWhiteSpace(val)) throw new ArgumentException($"{key} is required");
        return val;
    }

    private string ArgOpt(JObject args, string key) => args?[key]?.ToString();

    private async Task<HttpResponseMessage> CallGraphAsync(string method, string path, string body = null)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";

        var httpMethod = method switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => new HttpMethod("PATCH"),
            "PUT" => HttpMethod.Put,
            _ => HttpMethod.Get
        };

        var request = new HttpRequestMessage(httpMethod, url);
        request.Headers.Add("Accept", "application/json");

        if (!string.IsNullOrEmpty(body) && method != "GET" && method != "DELETE")
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> CallGraphForToolAsync(string method, string path, string body, JToken id)
    {
        var response = await CallGraphAsync(method, path, body).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var textOut = string.IsNullOrEmpty(content) ? $"Status: {(int)response.StatusCode} {response.StatusCode}" : content;
        return CreateToolResult(textOut, !response.IsSuccessStatusCode, id);
    }

    #endregion

    #region Application Insights Telemetry

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
                return;

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");
            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Suppress telemetry errors */ }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("InstrumentationKey=".Length);
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("IngestionEndpoint=".Length);
        return "https://dc.services.visualstudio.com/";
    }

    #endregion

    #region MCP Response Helpers

    private HttpResponseMessage CreateToolResult(string text, bool isError, JToken id)
    {
        return CreateMCPSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError
        }, id);
    }

    private HttpResponseMessage CreateMCPSuccess(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        return resp;
    }

    private HttpResponseMessage CreateMCPError(JToken id, int code, string message, string data = null)
    {
        var err = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrEmpty(data)) err["data"] = data;
        var json = new JObject { ["jsonrpc"] = "2.0", ["error"] = err, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        return resp;
    }

    #endregion
}
