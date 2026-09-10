using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string SERVER_NAME = "power-platform-capacity-intelligence";
    private const string SERVER_VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2025-11-25";

    private const string API_BASE = "https://api.powerplatform.com";
    private const string API_VERSION = "2024-10-01";

    // Paged endpoints are exposed one page at a time. Walking a tenant-wide collection
    // inside the two-minute script budget risks a timeout that looks like a failure,
    // so the continuation token is returned to the caller instead.
    private const int DEFAULT_PAGE_SIZE = 100;
    private const int MAX_PAGE_SIZE = 1000;

    // Verified against the live service: fromDate/toDate accept ISO yyyy-MM-dd. The
    // reference types them only as "string" with no format.
    private const string DATE_FORMAT = "yyyy-MM-dd";

    private const string APP_INSIGHTS_CONNECTION_STRING = "[INSERT_YOUR_APP_INSIGHTS_CONNECTION_STRING]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            switch (this.Context.OperationId)
            {
                case "InvokeMCP": return await HandleMcpEntryAsync().ConfigureAwait(false);

                case "GetFinOpsLicenseSummary":
                    return await Typed(HandleFinOpsLicenseSummary(new JObject())).ConfigureAwait(false);
                case "GetEntitlement":
                    return await Typed(HandleGetEntitlement(FromQuery("entitlementId"))).ConfigureAwait(false);
                case "ListEnvironmentEntitlements":
                    return await Typed(HandleListEnvironmentEntitlements(FromQuery("environmentId", "filter"))).ConfigureAwait(false);
                case "GetLicenseTrends":
                    return await Typed(HandleGetLicenseTrends(FromQuery("entitlementId", "fromDate", "toDate", "top"))).ConfigureAwait(false);
                case "ListResourceThresholds":
                    return await Typed(HandleListResourceThresholds(FromQuery("entitlementId", "environmentId"))).ConfigureAwait(false);
                case "ListTenantUsers":
                    return await Typed(HandleListTenantUsers(FromQuery("entitlementId", "fromDate", "toDate", "pageSize", "continuationToken"))).ConfigureAwait(false);
                case "GetUserConsumption":
                    return await Typed(HandleGetUserConsumption(FromQuery("entitlementId", "userId", "fromDate", "toDate", "pageSize", "continuationToken"))).ConfigureAwait(false);
                case "GetResourceConsumers":
                    return await Typed(HandleGetResourceConsumers(FromQuery("entitlementId", "resourceId", "fromDate", "toDate", "pageSize", "continuationToken"))).ConfigureAwait(false);
                case "GetAllocationAvailability":
                    return await Typed(HandleGetAllocationAvailability(FromQuery("entitlementId", "environmentId"))).ConfigureAwait(false);
                case "GetEnvironmentResources":
                    return await Typed(HandleGetEnvironmentResources(FromQuery("entitlementId", "environmentId", "fromDate", "toDate", "continuationToken"))).ConfigureAwait(false);
                case "GetTenantResources":
                    return await Typed(HandleGetTenantResources(FromQuery("entitlementId", "fromDate", "toDate", "pageSize", "continuationToken"))).ConfigureAwait(false);
                case "GetEntitlementPosture":
                    return await Typed(HandleGetEntitlementPosture(FromQuery("entitlementId"))).ConfigureAwait(false);

                case "GetEnvironmentDropdown": return await HandleEnvironmentDropdownAsync().ConfigureAwait(false);
                case "GetEntitlementDropdown": return await HandleEntitlementDropdownAsync().ConfigureAwait(false);

                default:
                    return CreateJsonRpcErrorResponse(null, -32601, $"Unknown operation: {this.Context.OperationId}");
            }
        }
        catch (ArgumentException ex)
        {
            return TypedError(ex.Message, 400);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Unhandled error: {ex.Message}");
            return TypedError(ex.Message, 500);
        }
    }

    // ─── MCP ────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpEntryAsync()
    {
        var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            return CreateJsonRpcErrorResponse(null, -32600, "Request body is required");

        JObject payload;
        try { payload = JObject.Parse(raw); }
        catch (JsonException ex) { return CreateJsonRpcErrorResponse(null, -32700, $"Parse error: {ex.Message}"); }

        var method = payload.Value<string>("method");
        var id = payload["id"];
        var args = payload["params"] as JObject ?? new JObject();

        switch (method)
        {
            case "initialize":
                return CreateJsonRpcSuccessResponse(id, new JObject
                {
                    ["protocolVersion"] = args.Value<string>("protocolVersion") ?? PROTOCOL_VERSION,
                    ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JObject { ["name"] = SERVER_NAME, ["version"] = SERVER_VERSION }
                });

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
            case "ping":
                return CreateJsonRpcSuccessResponse(id, new JObject());

            case "tools/list":
                return CreateJsonRpcSuccessResponse(id, new JObject { ["tools"] = GetToolDefinitions() });

            case "resources/list":
                return CreateJsonRpcSuccessResponse(id, new JObject { ["resources"] = new JArray() });

            case "tools/call":
                return await HandleToolCallAsync(args, id).ConfigureAwait(false);

            default:
                return CreateJsonRpcErrorResponse(id, -32601, $"Method not found: {method}");
        }
    }

    private async Task<HttpResponseMessage> HandleToolCallAsync(JObject @params, JToken id)
    {
        var tool = @params.Value<string>("name");
        var args = @params["arguments"] as JObject ?? new JObject();

        try
        {
            JToken result;
            switch ((tool ?? string.Empty).ToLowerInvariant())
            {
                case "capacity_get_finops_license_summary": result = await HandleFinOpsLicenseSummary(args).ConfigureAwait(false); break;
                case "capacity_get_entitlement": result = await HandleGetEntitlement(args).ConfigureAwait(false); break;
                case "capacity_list_environment_entitlements": result = await HandleListEnvironmentEntitlements(args).ConfigureAwait(false); break;
                case "capacity_get_license_trends": result = await HandleGetLicenseTrends(args).ConfigureAwait(false); break;
                case "capacity_list_resource_thresholds": result = await HandleListResourceThresholds(args).ConfigureAwait(false); break;
                case "capacity_list_tenant_users": result = await HandleListTenantUsers(args).ConfigureAwait(false); break;
                case "capacity_get_user_consumption": result = await HandleGetUserConsumption(args).ConfigureAwait(false); break;
                case "capacity_get_resource_consumers": result = await HandleGetResourceConsumers(args).ConfigureAwait(false); break;
                case "capacity_get_allocation_availability": result = await HandleGetAllocationAvailability(args).ConfigureAwait(false); break;
                case "capacity_get_environment_resources": result = await HandleGetEnvironmentResources(args).ConfigureAwait(false); break;
                case "capacity_get_tenant_resources": result = await HandleGetTenantResources(args).ConfigureAwait(false); break;
                case "capacity_get_entitlement_posture": result = await HandleGetEntitlementPosture(args).ConfigureAwait(false); break;

                default:
                    return CreateJsonRpcErrorResponse(id, -32602, $"Unknown tool: {tool}");
            }

            // Telemetry deliberately records the tool and entitlement only. These
            // endpoints return user IDs and per-user consumption, which must not leave
            // the tenant in a telemetry payload.
            await LogToAppInsights("ToolCall", new Dictionary<string, string>
            {
                ["Tool"] = tool ?? string.Empty,
                ["EntitlementId"] = args.Value<string>("entitlementId") ?? string.Empty
            }).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented) }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("ToolCallError", new Dictionary<string, string>
            {
                ["Tool"] = tool ?? string.Empty,
                ["Error"] = ex.Message
            }).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = ex.Message } },
                ["isError"] = true
            });
        }
    }

    // ─── Handlers ───────────────────────────────────────────────────────

    private async Task<JToken> HandleFinOpsLicenseSummary(JObject args)
    {
        var body = await GetAsync("/licensing/FinOpsLicensing/GetLicenseSummaryV2", null, "the Finance and Operations license summary")
            .ConfigureAwait(false);

        var doc = body as JObject ?? new JObject();

        // A tenant with no Finance and Operations estate returns zeroed counters and a
        // default timestamp rather than 204, so say so instead of implying real zeros.
        // Json.NET parses the timestamp into a Date token, so compare as a date rather
        // than by string prefix.
        var neverRefreshed = IsDefaultTimestamp(doc["lastReportRefreshTime"]);

        return new JObject
        {
            ["tenantId"] = doc["tenantId"],
            ["hasData"] = !neverRefreshed,
            ["lastReportRefreshTime"] = neverRefreshed ? (JToken)JValue.CreateNull() : doc["lastReportRefreshTime"],
            ["allUsersCount"] = doc["allUsersCount"],
            ["usersWithoutLicensesCount"] = doc["usersWithoutLicensesCount"],
            ["underLicensedUsersCount"] = doc["underLicensedUsersCount"],
            ["overLicensedUsersCount"] = doc["overLicensedUsersCount"],
            ["licensesConsumption"] = doc["licensesConsumption"],
            ["message"] = neverRefreshed
                ? "This tenant has no Finance and Operations licensing report. The zero counts are the absence of a report, not a measured result."
                : "Returned the current Finance and Operations license summary."
        };
    }

    private async Task<JToken> HandleGetEntitlement(JObject args)
    {
        var entitlementId = RequireEntitlementId(args);

        var body = await GetAsync(
            $"/licensing/entitlements/{Uri.EscapeDataString(entitlementId)}",
            null,
            $"entitlement {entitlementId}").ConfigureAwait(false);

        var doc = body as JObject ?? new JObject();
        var capacity = doc.SelectToken("entitlement.capacity") as JObject;

        return new JObject
        {
            ["entitlementId"] = doc["entitlementId"] ?? entitlementId,
            ["unit"] = doc.SelectToken("entitlement.unit"),
            ["entitled"] = capacity == null ? null : capacity.SelectToken("entitled.value"),
            ["consumed"] = capacity == null ? null : capacity.SelectToken("consumed.value"),
            ["consumptionType"] = capacity == null ? null : capacity.SelectToken("consumed.consumptionType"),
            ["lastUpdatedOn"] = capacity == null ? null : capacity.SelectToken("consumed.lastUpdatedOn"),
            ["allocated"] = capacity == null ? null : capacity.SelectToken("allocated.value"),
            ["autoAllocated"] = capacity == null ? null : capacity.SelectToken("allocated.autoAllocated"),
            ["availableQuantity"] = capacity == null ? null : capacity["availableQuantity"],
            ["status"] = capacity == null ? null : capacity["status"],
            ["payGoEntitled"] = doc.SelectToken("entitlement.payGo.entitled.value"),
            ["productCategories"] = doc["productCategories"] ?? new JArray(),
            ["licenses"] = capacity == null ? new JArray() : (capacity["licenses"] ?? new JArray())
        };
    }

    private async Task<JToken> HandleListEnvironmentEntitlements(JObject args)
    {
        var environmentId = RequireEnvironmentId(args);
        var filter = args.Value<string>("filter");

        var query = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(filter)) query["$filter"] = filter.Trim();

        var body = await GetAsync(
            $"/licensing/environments/{Uri.EscapeDataString(environmentId)}/entitlements",
            query,
            $"entitlements for environment {environmentId}").ConfigureAwait(false);

        var summary = new JArray();

        foreach (var row in AsArray(body).OfType<JObject>())
        {
            var capacity = row.SelectToken("entitlement.capacity") as JObject;
            summary.Add(new JObject
            {
                ["entitlementId"] = row["entitlementId"],
                ["unit"] = row.SelectToken("entitlement.unit"),
                ["entitled"] = capacity == null ? null : capacity.SelectToken("entitled.value"),
                ["consumed"] = capacity == null ? null : capacity.SelectToken("consumed.value"),
                ["allocated"] = capacity == null ? null : capacity.SelectToken("allocated.value"),
                ["availableQuantity"] = capacity == null ? null : capacity["availableQuantity"],
                ["status"] = capacity == null ? null : capacity["status"],
                ["enforcementRules"] = SummarizeRules(capacity == null ? null : capacity["enforcementRules"] as JArray)
            });
        }

        return new JObject
        {
            ["environmentId"] = environmentId,
            ["entitlementCount"] = summary.Count,
            ["entitlements"] = summary
        };
    }

    private async Task<JToken> HandleGetLicenseTrends(JObject args)
    {
        var entitlementId = RequireEntitlementId(args);
        var range = ResolveDateRange(args);

        var query = new Dictionary<string, string>
        {
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2
        };

        var top = args["top"];
        if (top != null && top.Type != JTokenType.Null) query["$top"] = top.ToString();

        var body = await GetAsync(
            $"/licensing/entitlements/{Uri.EscapeDataString(entitlementId)}/licenses",
            query,
            $"license trends for {entitlementId}").ConfigureAwait(false);

        var page = ReadPage(body);

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["pointCount"] = page.Item1.Count,
            ["trends"] = page.Item1,
            ["continuationToken"] = page.Item2,
            ["hasMore"] = !string.IsNullOrEmpty(page.Item2),
            ["message"] = page.Item1.Count == 0
                ? "No license trend points were returned for this range. Capacity-model entitlements with no assigned licenses report nothing here."
                : null
        };
    }

    private async Task<JToken> HandleListResourceThresholds(JObject args)
    {
        var entitlementId = RequireEntitlementId(args);
        var environmentFilter = args.Value<string>("environmentId");

        var body = await GetAsync(
            $"/licensing/entitlements/{Uri.EscapeDataString(entitlementId)}/resourceThresholds",
            null,
            $"resource thresholds for {entitlementId}").ConfigureAwait(false);

        var rows = AsArray(body);

        if (!string.IsNullOrWhiteSpace(environmentFilter))
        {
            var filtered = new JArray();
            foreach (var row in rows.OfType<JObject>())
            {
                if (string.Equals(row.Value<string>("environmentId"), environmentFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                    filtered.Add(row);
            }
            rows = filtered;
        }

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["environmentId"] = string.IsNullOrWhiteSpace(environmentFilter) ? (JToken)JValue.CreateNull() : environmentFilter,
            ["thresholdCount"] = rows.Count,
            ["thresholds"] = rows,
            ["message"] = rows.Count == 0
                ? "No thresholds are configured. Consumption is not capped for this entitlement."
                : null
        };
    }

    private async Task<JToken> HandleListTenantUsers(JObject args)
    {
        var entitlementId = RequireEntitlementId(args);
        var range = ResolveDateRange(args);

        var query = new Dictionary<string, string>
        {
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["pageSize"] = ResolvePageSize(args).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        AddContinuation(query, args);

        var body = await GetAsync(
            $"/licensing/entitlements/{Uri.EscapeDataString(entitlementId)}/users",
            query,
            $"tenant users for {entitlementId}").ConfigureAwait(false);

        var page = ReadPage(body);
        var users = FlattenNested(page.Item1, "users");

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["userCount"] = users.Count,
            ["users"] = users,
            ["continuationToken"] = page.Item2,
            ["hasMore"] = !string.IsNullOrEmpty(page.Item2),
            ["privacyNote"] = "Rows identify individual users. Handle and retain this data according to your privacy obligations."
        };
    }

    private async Task<JToken> HandleGetUserConsumption(JObject args)
    {
        var entitlementId = RequireEntitlementId(args);
        var userId = RequireValue(args, "userId", "userId is required. Use capacity_list_tenant_users to find user IDs.");
        var range = ResolveDateRange(args);

        var query = new Dictionary<string, string>
        {
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["pageSize"] = ResolvePageSize(args).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        AddContinuation(query, args);

        var body = await GetAsync(
            $"/licensing/entitlements/{Uri.EscapeDataString(entitlementId)}/users/{Uri.EscapeDataString(userId)}/resources",
            query,
            $"consumption for user {userId}").ConfigureAwait(false);

        var page = ReadPage(body);
        var resources = FlattenNested(page.Item1, "resources");

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["userId"] = userId,
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["resourceCount"] = resources.Count,
            ["resources"] = resources,
            ["continuationToken"] = page.Item2,
            ["hasMore"] = !string.IsNullOrEmpty(page.Item2)
        };
    }

    private async Task<JToken> HandleGetResourceConsumers(JObject args)
    {
        var entitlementId = RequireEntitlementId(args);
        var resourceId = RequireValue(args, "resourceId", "resourceId is required.");
        var range = ResolveDateRange(args);

        var query = new Dictionary<string, string>
        {
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["pageSize"] = ResolvePageSize(args).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        AddContinuation(query, args);

        var body = await GetAsync(
            $"/licensing/entitlements/{Uri.EscapeDataString(entitlementId)}/resources/{Uri.EscapeDataString(resourceId)}/users",
            query,
            $"consumers of resource {resourceId}").ConfigureAwait(false);

        var page = ReadPage(body);
        var users = FlattenNested(page.Item1, "users");

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["resourceId"] = resourceId,
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["userCount"] = users.Count,
            ["users"] = users,
            ["continuationToken"] = page.Item2,
            ["hasMore"] = !string.IsNullOrEmpty(page.Item2),
            ["privacyNote"] = "Rows identify individual users. Handle and retain this data according to your privacy obligations."
        };
    }

    private async Task<JToken> HandleGetAllocationAvailability(JObject args)
    {
        // Verified against the live service: this endpoint returns 400 "Invalid filter
        // options" when called without a filter, even though the reference marks $filter
        // optional. entitlementId is therefore required and the filter is built here
        // rather than left to the caller.
        var entitlementId = RequireEntitlementId(args);
        var environmentId = args.Value<string>("environmentId");

        var filter = $"entitlementId eq '{EscapeODataLiteral(entitlementId)}'";
        if (!string.IsNullOrWhiteSpace(environmentId))
            filter = $"environmentId eq '{EscapeODataLiteral(environmentId.Trim())}' and {filter}";

        var body = await GetAsync(
            "/licensing/allocationsV2/availability",
            new Dictionary<string, string> { ["$filter"] = filter },
            $"allocation availability for {entitlementId}").ConfigureAwait(false);

        var doc = body as JObject ?? new JObject();
        var available = doc["entitlementAllocationsAvailable"] as JArray ?? new JArray();
        var first = available.FirstOrDefault() as JObject;

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["environmentId"] = string.IsNullOrWhiteSpace(environmentId) ? (JToken)JValue.CreateNull() : environmentId,
            ["scope"] = doc["scope"],
            ["available"] = available,
            ["availableQuantity"] = first == null ? null : first["availableQuantity"]
        };
    }

    private async Task<JToken> HandleGetEnvironmentResources(JObject args)
    {
        var entitlementId = RequireEntitlementId(args);
        var environmentId = RequireEnvironmentId(args);
        var range = ResolveDateRange(args);

        var query = new Dictionary<string, string>
        {
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2
        };
        AddContinuation(query, args);

        var body = await GetAsync(
            $"/licensing/entitlements/{Uri.EscapeDataString(entitlementId)}/environments/{Uri.EscapeDataString(environmentId)}/resources",
            query,
            $"resource consumption in environment {environmentId}").ConfigureAwait(false);

        var page = ReadPage(body);
        var resources = FlattenNested(page.Item1, "resources");

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["environmentId"] = environmentId,
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["resourceCount"] = resources.Count,
            ["resources"] = resources,
            ["continuationToken"] = page.Item2,
            ["hasMore"] = !string.IsNullOrEmpty(page.Item2)
        };
    }

    private async Task<JToken> HandleGetTenantResources(JObject args)
    {
        var entitlementId = RequireEntitlementId(args);
        var range = ResolveDateRange(args);

        var query = new Dictionary<string, string>
        {
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["pageSize"] = ResolvePageSize(args).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        AddContinuation(query, args);

        var body = await GetAsync(
            $"/licensing/entitlements/{Uri.EscapeDataString(entitlementId)}/resources",
            query,
            $"tenant-wide resource consumption for {entitlementId}").ConfigureAwait(false);

        var page = ReadPage(body);
        var resources = FlattenNested(page.Item1, "resources");

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["fromDate"] = range.Item1,
            ["toDate"] = range.Item2,
            ["resourceCount"] = resources.Count,
            ["resources"] = resources,
            ["continuationToken"] = page.Item2,
            ["hasMore"] = !string.IsNullOrEmpty(page.Item2)
        };
    }

    /// <summary>
    /// Composite read: entitlement capacity, unallocated headroom, and configured
    /// thresholds in one call. Each part degrades independently so one permission gap
    /// does not blank the whole picture.
    /// </summary>
    private async Task<JToken> HandleGetEntitlementPosture(JObject args)
    {
        var entitlementId = RequireEntitlementId(args);
        var problems = new JArray();

        JToken entitlement = null;
        JToken availability = null;
        JToken thresholds = null;

        try { entitlement = await HandleGetEntitlement(args).ConfigureAwait(false); }
        catch (Exception ex) { problems.Add("entitlement: " + ex.Message); }

        try { availability = await HandleGetAllocationAvailability(args).ConfigureAwait(false); }
        catch (Exception ex) { problems.Add("availability: " + ex.Message); }

        try { thresholds = await HandleListResourceThresholds(args).ConfigureAwait(false); }
        catch (Exception ex) { problems.Add("thresholds: " + ex.Message); }

        var entitled = ReadNumber(entitlement, "entitled");
        var consumed = ReadNumber(entitlement, "consumed");

        double? percentUsed = null;
        if (entitled.HasValue && consumed.HasValue && entitled.Value > 0)
            percentUsed = Math.Round(consumed.Value / entitled.Value * 100, 2);

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["entitled"] = entitled,
            ["consumed"] = consumed,
            ["percentUsed"] = percentUsed,
            ["status"] = entitlement == null ? null : entitlement["status"],
            ["consumptionType"] = entitlement == null ? null : entitlement["consumptionType"],
            ["lastUpdatedOn"] = entitlement == null ? null : entitlement["lastUpdatedOn"],
            ["availability"] = availability,
            ["thresholds"] = thresholds,
            ["problems"] = problems,
            ["complete"] = problems.Count == 0,
            ["note"] = "percentUsed is computed from entitled and consumed as reported by the service. " +
                       "consumptionType says what the consumed figure covers - MonthToDate resets monthly, " +
                       "so it is not a running total. This is a measurement, not a forecast."
        };
    }

    /// <summary>
    /// True when a timestamp is absent or is the default DateTime the service returns
    /// for a report it has never produced.
    /// </summary>
    private static bool IsDefaultTimestamp(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return true;

        if (token.Type == JTokenType.Date)
        {
            var value = token.Value<DateTime>();
            return value.Year <= 1;
        }

        var text = token.ToString();
        if (string.IsNullOrWhiteSpace(text)) return true;

        DateTime parsed;
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out parsed))
        {
            return parsed.Year <= 1;
        }

        return false;
    }

    private static double? ReadNumber(JToken source, string name)
    {
        var token = source == null ? null : source[name];
        if (token == null || token.Type == JTokenType.Null) return null;
        if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) return token.Value<double>();

        double parsed;
        if (double.TryParse(token.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsed)) return parsed;
        return null;
    }

    // ─── HTTP ───────────────────────────────────────────────────────────

    private async Task<JToken> GetAsync(string path, IDictionary<string, string> query, string description)
    {
        var url = API_BASE + path + "?api-version=" + API_VERSION;
        if (query != null)
        {
            foreach (var kvp in query)
            {
                if (string.IsNullOrWhiteSpace(kvp.Value)) continue;
                url += "&" + Uri.EscapeDataString(kvp.Key) + "=" + Uri.EscapeDataString(kvp.Value);
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/json");
        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var body = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent) return null;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(DescribeFailure(response.StatusCode, body, description));

        if (string.IsNullOrWhiteSpace(body)) return null;

        try { return JToken.Parse(body); }
        catch (JsonException) { return null; }
    }

    private static string DescribeFailure(HttpStatusCode status, string body, string description)
    {
        // Several licensing endpoints return 403 with an empty body, so the connector has
        // to supply the diagnosis the service withholds.
        if (status == HttpStatusCode.Forbidden)
        {
            return $"Access denied reading {description} (403). " +
                   (string.IsNullOrWhiteSpace(body)
                       ? "The service returned no detail. Grant Licensing.Allocations.Read to the app registration and sign in as a Power Platform or Global administrator. " +
                         "Some per-resource consumption endpoints stay denied even for tenant administrators; if a sibling entitlement read succeeds, the permission is present and this endpoint is restricted separately."
                       : Truncate(body));
        }

        if (status == HttpStatusCode.NotFound)
            return $"{Capitalize(description)} was not found (404). Verify the entitlement and environment identifiers.";

        if (status == HttpStatusCode.BadRequest)
            return $"The service rejected the request for {description} (400): {Truncate(body)}";

        if ((int)status == 429)
            return $"Throttled reading {description} (429). Retry after a short delay.";

        return $"Reading {description} returned {(int)status}: {Truncate(body)}";
    }

    // ─── Shaping helpers ────────────────────────────────────────────────

    private static JArray AsArray(JToken body)
    {
        if (body == null) return new JArray();
        var array = body as JArray;
        if (array != null) return array;

        var envelope = body as JObject;
        if (envelope != null)
        {
            var value = envelope["value"] as JArray;
            if (value != null) return value;
        }

        return new JArray();
    }

    /// <summary>
    /// Paged licensing responses carry `value` plus a lowercase `continuationtoken`.
    /// The reference also documents `@odata.nextLink`, which the live service does not
    /// return, so both spellings are accepted.
    /// </summary>
    private static Tuple<JArray, string> ReadPage(JToken body)
    {
        var envelope = body as JObject;
        if (envelope == null) return Tuple.Create(AsArray(body), (string)null);

        var token = envelope.Value<string>("continuationtoken")
                    ?? envelope.Value<string>("continuationToken")
                    ?? envelope.Value<string>("@odata.nextLink");

        return Tuple.Create(
            envelope["value"] as JArray ?? new JArray(),
            string.IsNullOrWhiteSpace(token) ? null : token);
    }

    // Tenant user and resource pages wrap their rows one level deeper, as
    // value[].users[] or value[].resources[].
    private static JArray FlattenNested(JArray page, string childName)
    {
        var flat = new JArray();
        foreach (var item in page)
        {
            var container = item as JObject;
            if (container == null) continue;

            var children = container[childName] as JArray;
            if (children == null) { flat.Add(item); continue; }

            foreach (var child in children) flat.Add(child);
        }
        return flat;
    }

    private static JObject SummarizeRules(JArray rules)
    {
        var summary = new JObject();
        if (rules == null) return summary;

        foreach (var item in rules.OfType<JObject>())
        {
            var type = item.Value<string>("ruleType");
            if (!string.IsNullOrEmpty(type)) summary[type] = item["enabled"];
        }
        return summary;
    }

    // ─── Argument helpers ───────────────────────────────────────────────

    private static string RequireEntitlementId(JObject args)
    {
        return RequireValue(args, "entitlementId",
            "entitlementId is required, for example MCSMessages, AI, or PowerPagesAuthenticated. " +
            "Use capacity_list_environment_entitlements to discover the entitlements in an environment.");
    }

    private static string RequireEnvironmentId(JObject args)
    {
        return RequireValue(args, "environmentId", "environmentId is required.");
    }

    private static string RequireValue(JObject args, string name, string message)
    {
        var value = args.Value<string>(name);
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message);
        return value.Trim();
    }

    /// <summary>
    /// Defaults to month-to-date, which matches how the service reports consumption for
    /// capacity entitlements. Verified format is ISO yyyy-MM-dd.
    /// </summary>
    private static Tuple<string, string> ResolveDateRange(JObject args)
    {
        var today = DateTime.UtcNow.Date;
        var from = NormalizeDate(args.Value<string>("fromDate"), "fromDate")
                   ?? new DateTime(today.Year, today.Month, 1);
        var to = NormalizeDate(args.Value<string>("toDate"), "toDate") ?? today;

        if (from > to)
            throw new ArgumentException("fromDate must be on or before toDate.");

        return Tuple.Create(
            from.ToString(DATE_FORMAT, System.Globalization.CultureInfo.InvariantCulture),
            to.ToString(DATE_FORMAT, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static DateTime? NormalizeDate(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        DateTime parsed;
        if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out parsed))
            return parsed.Date;

        throw new ArgumentException($"{name} could not be read as a date. Use ISO format, for example 2026-09-01.");
    }

    private static int ResolvePageSize(JObject args)
    {
        var token = args["pageSize"];
        if (token == null || token.Type == JTokenType.Null) return DEFAULT_PAGE_SIZE;

        int size;
        if (!int.TryParse(token.ToString(), out size))
            throw new ArgumentException("pageSize must be a whole number.");

        if (size < 1) throw new ArgumentException("pageSize must be at least 1.");
        return Math.Min(size, MAX_PAGE_SIZE);
    }

    private static void AddContinuation(IDictionary<string, string> query, JObject args)
    {
        var token = args.Value<string>("continuationToken");
        if (!string.IsNullOrWhiteSpace(token)) query["continuationToken"] = token.Trim();
    }

    // An OData string literal escapes a single quote by doubling it.
    private static string EscapeODataLiteral(string value)
    {
        return value == null ? string.Empty : value.Replace("'", "''");
    }

    // ─── Dropdowns ──────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleEnvironmentDropdownAsync()
    {
        var body = await GetAsync("/environmentmanagement/environments", null, "the environment list").ConfigureAwait(false);
        var dropdown = new JArray();

        foreach (var env in AsArray(body).OfType<JObject>())
        {
            var id = env.Value<string>("id");
            if (string.IsNullOrEmpty(id)) continue;

            // The environments API reports these at the top level; older payloads nest
            // them under properties. Read flat first so the picker shows names, not GUIDs.
            var name = FirstValue(env, "displayName", "properties.displayName", "name");
            dropdown.Add(new JObject
            {
                ["id"] = id,
                ["name"] = name == null ? id : name.ToString()
            });
        }

        return CreateTypedResponse(dropdown);
    }

    private async Task<HttpResponseMessage> HandleEntitlementDropdownAsync()
    {
        var environmentId = GetQueryParam("environmentId");
        if (string.IsNullOrWhiteSpace(environmentId)) return CreateTypedResponse(new JArray());

        var body = await GetAsync(
            $"/licensing/environments/{Uri.EscapeDataString(environmentId)}/entitlements",
            null,
            "the entitlement list").ConfigureAwait(false);

        var dropdown = new JArray();
        foreach (var row in AsArray(body).OfType<JObject>())
        {
            var id = row.Value<string>("entitlementId");
            if (string.IsNullOrEmpty(id)) continue;
            dropdown.Add(new JObject { ["id"] = id, ["name"] = id });
        }

        return CreateTypedResponse(dropdown);
    }

    private static JToken FirstValue(JToken source, params string[] paths)
    {
        if (source == null) return null;

        foreach (var path in paths)
        {
            JToken token;
            try { token = source.SelectToken(path); }
            catch (JsonException) { continue; }

            if (token != null && token.Type != JTokenType.Null) return token;
        }
        return null;
    }

    // ─── Typed plumbing ─────────────────────────────────────────────────

    private JObject FromQuery(params string[] names)
    {
        var args = new JObject();
        foreach (var name in names)
        {
            var value = GetQueryParam(name);
            if (!string.IsNullOrWhiteSpace(value)) args[name] = value;
        }
        return args;
    }

    private string GetQueryParam(string name)
    {
        var uri = this.Context.Request.RequestUri;
        var query = uri == null ? null : uri.Query;
        if (string.IsNullOrEmpty(query)) return null;

        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            if (pair.Length == 0) continue;
            var parts = pair.Split(new[] { '=' }, 2);
            if (string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
                return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }
        return null;
    }

    private async Task<HttpResponseMessage> Typed(Task<JToken> work)
    {
        var result = await work.ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private HttpResponseMessage CreateTypedResponse(JToken result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                (result ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage TypedError(string message, int statusCode)
    {
        return new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(
                new JObject { ["error"] = message }.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id }
                    .ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["error"] = new JObject { ["code"] = code, ["message"] = message },
                    ["id"] = id
                }.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8, "application/json")
        };
    }

    // ─── Telemetry ──────────────────────────────────────────────────────

    private async Task LogToAppInsights(string eventName, Dictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_CONNECTION_STRING) ||
            APP_INSIGHTS_CONNECTION_STRING.Contains("INSERT_YOUR")) return;

        try
        {
            var iKey = string.Empty;
            foreach (var part in APP_INSIGHTS_CONNECTION_STRING.Split(';'))
            {
                if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                {
                    iKey = part.Substring("InstrumentationKey=".Length);
                    break;
                }
            }
            if (string.IsNullOrEmpty(iKey)) return;

            var telemetry = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = iKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = SERVER_NAME + "." + eventName,
                        ["properties"] = properties == null ? new JObject() : JObject.FromObject(properties)
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    telemetry.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Telemetry must never fail the call. */ }
    }

    // ─── Utility ────────────────────────────────────────────────────────

    private static string Truncate(string text, int max = 400)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        return text.Substring(0, max) + "... (truncated)";
    }

    private static string Capitalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    // ─── Tool definitions ───────────────────────────────────────────────

    private static JObject Prop(string description, string type = "string")
    {
        return new JObject { ["type"] = type, ["description"] = description };
    }

    private static JObject Tool(string name, string description, JObject properties, params string[] required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties ?? new JObject(),
                ["required"] = new JArray(required ?? new string[0])
            }
        };
    }

    private static JObject DateProps(JObject extra)
    {
        var props = new JObject
        {
            ["fromDate"] = Prop("Start of the range, inclusive, as yyyy-MM-dd. Defaults to the first of the current month."),
            ["toDate"] = Prop("End of the range, inclusive, as yyyy-MM-dd. Defaults to today."),
            ["pageSize"] = Prop("Rows per page, 1-1000. Defaults to 100.", "integer"),
            ["continuationToken"] = Prop("Token from a previous response. Pass it back to fetch the next page.")
        };

        if (extra != null)
            foreach (var p in extra.Properties()) props[p.Name] = p.Value;

        return props;
    }

    private static JArray GetToolDefinitions()
    {
        var entitlement = Prop("The entitlement ID, for example MCSMessages, AI, or PowerPagesAuthenticated.");
        var environment = Prop("The environment ID (GUID).");

        return new JArray
        {
            Tool("capacity_get_finops_license_summary",
                "Get the tenant Finance and Operations license summary: total users, and how many are unlicensed, under-licensed, or over-licensed. Returns hasData false when the tenant has no Finance and Operations estate, so zero counts are not mistaken for a measured result. Read-only.",
                new JObject()),

            Tool("capacity_get_entitlement",
                "Get tenant-wide capacity for one entitlement: entitled, consumed, allocated, available, and overage status. Start here to answer how much of something the tenant has and has used. Read-only.",
                new JObject { ["entitlementId"] = entitlement },
                "entitlementId"),

            Tool("capacity_list_environment_entitlements",
                "List every entitlement available to one environment with its capacity, consumption, and enforcement rules. Use this to discover which entitlement IDs exist before calling the other tools. Read-only.",
                new JObject
                {
                    ["environmentId"] = environment,
                    ["filter"] = Prop("Optional OData filter. The filterable fields are not published; prefer omitting it.")
                },
                "environmentId"),

            Tool("capacity_get_entitlement_posture",
                "Combined read for one entitlement: capacity, unallocated headroom, and configured thresholds, with percent used. Each part degrades independently, so a permission gap on one does not blank the rest - check the problems array. Read-only.",
                new JObject { ["entitlementId"] = entitlement },
                "entitlementId"),

            Tool("capacity_get_allocation_availability",
                "Get how much of an entitlement is still available to allocate. Call this before increasing an environment allocation to confirm the tenant has headroom. Read-only.",
                new JObject
                {
                    ["entitlementId"] = entitlement,
                    ["environmentId"] = Prop("Optional. Narrows the availability scope to one environment.")
                },
                "entitlementId"),

            Tool("capacity_list_resource_thresholds",
                "List the consumption thresholds configured for an entitlement, with limits, current consumption, and stop flags. An empty result means consumption is not capped. Read-only.",
                new JObject
                {
                    ["entitlementId"] = entitlement,
                    ["environmentId"] = Prop("Optional. Filters the result to one environment.")
                },
                "entitlementId"),

            Tool("capacity_get_license_trends",
                "Get licence counts for an entitlement over a date range, by license model and tier. Capacity-model entitlements with no assigned licences return nothing. Read-only.",
                DateProps(new JObject
                {
                    ["entitlementId"] = entitlement,
                    ["top"] = Prop("Maximum records to return.", "integer")
                }),
                "entitlementId"),

            Tool("capacity_get_environment_resources",
                "Get per-resource consumption for one entitlement inside one environment - which agents, apps, or flows consumed the capacity. Read-only.",
                DateProps(new JObject { ["entitlementId"] = entitlement, ["environmentId"] = environment }),
                "entitlementId", "environmentId"),

            Tool("capacity_get_tenant_resources",
                "Get per-resource consumption for one entitlement across every environment in the tenant. Use this to find the largest consumers estate-wide. Read-only.",
                DateProps(new JObject { ["entitlementId"] = entitlement }),
                "entitlementId"),

            Tool("capacity_list_tenant_users",
                "List the users consuming an entitlement, with how much each consumed. Returns data about identifiable individuals - handle it according to your privacy obligations. Read-only.",
                DateProps(new JObject { ["entitlementId"] = entitlement }),
                "entitlementId"),

            Tool("capacity_get_user_consumption",
                "Get what one user consumed of an entitlement, broken down by resource. Returns data about an identifiable individual. Read-only.",
                DateProps(new JObject
                {
                    ["entitlementId"] = entitlement,
                    ["userId"] = Prop("The user ID. Use capacity_list_tenant_users to find one.")
                }),
                "entitlementId", "userId"),

            Tool("capacity_get_resource_consumers",
                "Get which users consumed a specific resource, such as one agent or app. Returns data about identifiable individuals. Read-only.",
                DateProps(new JObject
                {
                    ["entitlementId"] = entitlement,
                    ["resourceId"] = Prop("The resource ID, for example an agent or app identifier.")
                }),
                "entitlementId", "resourceId")
        };
    }
}
