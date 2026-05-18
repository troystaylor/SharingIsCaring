using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights configuration - set your connection string to enable telemetry
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // In-memory access token cache keyed by tenant+username
    private static readonly Dictionary<string, (string Token, DateTime Expiry)> _tokenCache =
        new Dictionary<string, (string, DateTime)>();
    private static readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            await LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                Path = Context.Request.RequestUri?.AbsolutePath ?? "unknown"
            });

            HttpResponseMessage response;

            switch (Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpAsync(correlationId);
                    break;
                default:
                    response = await ForwardToWfm();
                    break;
            }

            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                StatusCode = (int)response.StatusCode,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });

            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            });
            throw;
        }
    }

    #region Authentication

    /// <summary>
    /// Reads a connection-parameter value from a request header injected by a `setheader` policyTemplate.
    /// Returns null if the header is absent.
    /// </summary>
    private string GetConnParam(string headerName)
    {
        if (this.Context.Request.Headers.TryGetValues(headerName, out var values))
        {
            foreach (var v in values) return v;
        }
        return null;
    }

    /// <summary>
    /// Removes connection-parameter headers from the request before it is forwarded to the
    /// backend (so we don't leak credentials to the UKG WFM API).
    /// </summary>
    private void StripConnHeaders()
    {
        this.Context.Request.Headers.Remove("X-Connection-TenantUrl");
        this.Context.Request.Headers.Remove("X-Connection-Username");
        this.Context.Request.Headers.Remove("X-Connection-Password");
        this.Context.Request.Headers.Remove("X-Connection-AppKey");
    }

    private string GetTenantUrl()
    {
        var url = GetConnParam("X-Connection-TenantUrl") ?? "";
        return url.TrimEnd('/');
    }

    private async Task<string> GetAccessTokenAsync()
    {
        var tenantUrl = GetTenantUrl();
        var username = GetConnParam("X-Connection-Username");
        var password = GetConnParam("X-Connection-Password");
        var appKey = GetConnParam("X-Connection-AppKey");
        var cacheKey = $"{tenantUrl}|{username}";

        if (_tokenCache.TryGetValue(cacheKey, out var entry)
            && entry.Expiry > DateTime.UtcNow.AddSeconds(60))
        {
            return entry.Token;
        }

        await _tokenLock.WaitAsync(this.CancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock
            if (_tokenCache.TryGetValue(cacheKey, out entry)
                && entry.Expiry > DateTime.UtcNow.AddSeconds(60))
            {
                return entry.Token;
            }

            var tokenUrl = $"{tenantUrl}/api/authentication/access_token";
            var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            request.Headers.TryAddWithoutValidation("appkey", appKey);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("client_id", appKey),
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password)
            };
            request.Content = new FormUrlEncodedContent(form);

            var response = await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"WFM token exchange failed ({(int)response.StatusCode}): {body}");

            var json = JObject.Parse(body);
            var token = json["access_token"]?.ToString();
            if (string.IsNullOrEmpty(token))
                throw new HttpRequestException("WFM token exchange returned no access_token.");

            var expiresIn = json["expires_in"]?.Value<int>() ?? 1800;
            var expiry = DateTime.UtcNow.AddSeconds(expiresIn);
            _tokenCache[cacheKey] = (token, expiry);
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    #endregion

    #region WFM API Forwarding

    private async Task<HttpResponseMessage> ForwardToWfm()
    {
        var token = await GetAccessTokenAsync();
        var appKey = GetConnParam("X-Connection-AppKey");
        var tenantUrl = GetTenantUrl();

        var originalUri = this.Context.Request.RequestUri;
        var tenantBase = new Uri(tenantUrl);
        var rewritten = new UriBuilder(tenantBase)
        {
            Path = originalUri.AbsolutePath,
            Query = originalUri.Query.TrimStart('?')
        };
        this.Context.Request.RequestUri = rewritten.Uri;

        // Remove conn-param headers BEFORE adding auth + forwarding to backend.
        StripConnHeaders();

        this.Context.Request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        this.Context.Request.Headers.TryAddWithoutValidation("appkey", appKey);

        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<JToken> CallWfmApi(string method, string path, JObject body = null, JObject query = null)
    {
        var token = await GetAccessTokenAsync();
        var appKey = GetConnParam("X-Connection-AppKey");
        var tenantUrl = GetTenantUrl();

        var url = tenantUrl + path;
        if (query != null && query.HasValues)
        {
            var parts = new List<string>();
            foreach (var prop in query.Properties())
            {
                var val = prop.Value?.ToString();
                if (!string.IsNullOrEmpty(val))
                    parts.Add($"{prop.Name}={Uri.EscapeDataString(val)}");
            }
            if (parts.Count > 0)
                url += "?" + string.Join("&", parts);
        }

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("appkey", appKey);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            request.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

        var response = await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WFM API returned {(int)response.StatusCode}: {content}");

        if (string.IsNullOrEmpty(content))
            return new JObject();

        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("["))
            return JArray.Parse(content);
        return JObject.Parse(content);
    }

    #endregion

    #region MCP Protocol Handler

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId)
    {
        var body = await Context.Request.Content.ReadAsStringAsync();
        var request = JObject.Parse(body);

        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        await LogToAppInsights("MCPRequest", new
        {
            CorrelationId = correlationId,
            Method = method,
            HasParams = @params.HasValues
        });

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
                return await HandleToolsCall(@params, requestId, correlationId);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "ukg-pro-wfm-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // --- Common Resources ---
            CreateTool("retrieve_tenant_information", "Get tenant-level configuration details.",
                new JObject(), Array.Empty<string>()),

            CreateTool("get_current_user", "Get the person record for the authenticated user.",
                new JObject(), Array.Empty<string>()),

            CreateTool("retrieve_persons", "Retrieve all persons accessible to the authenticated user.",
                new JObject
                {
                    ["pageSize"] = new JObject { ["type"] = "integer", ["description"] = "Maximum results per page" },
                    ["pageNumber"] = new JObject { ["type"] = "integer", ["description"] = "Page number" }
                },
                Array.Empty<string>()),

            CreateTool("retrieve_persons_by_criteria", "Retrieve persons matching filter criteria (hyperfind or person numbers).",
                new JObject
                {
                    ["hyperfindId"] = new JObject { ["type"] = "string", ["description"] = "Hyperfind ID to filter persons" },
                    ["hyperfindQualifier"] = new JObject { ["type"] = "string", ["description"] = "Hyperfind qualifier (e.g. Public)" },
                    ["personNumbers"] = new JObject { ["type"] = "array", ["description"] = "List of person numbers", ["items"] = new JObject { ["type"] = "string" } }
                },
                Array.Empty<string>()),

            CreateTool("get_person_by_id", "Get a single person by person identity.",
                new JObject
                {
                    ["personIdentity"] = new JObject { ["type"] = "string", ["description"] = "Person identity (typically person number)" }
                },
                new[] { "personIdentity" }),

            CreateTool("retrieve_locations", "Retrieve organizational locations.",
                new JObject
                {
                    ["pageSize"] = new JObject { ["type"] = "integer", ["description"] = "Maximum results per page" },
                    ["pageNumber"] = new JObject { ["type"] = "integer", ["description"] = "Page number" }
                },
                Array.Empty<string>()),

            CreateTool("retrieve_jobs", "Retrieve job definitions.",
                new JObject
                {
                    ["pageSize"] = new JObject { ["type"] = "integer", ["description"] = "Maximum results per page" },
                    ["pageNumber"] = new JObject { ["type"] = "integer", ["description"] = "Page number" }
                },
                Array.Empty<string>()),

            CreateTool("retrieve_cost_centers", "Retrieve cost centers.",
                new JObject
                {
                    ["pageSize"] = new JObject { ["type"] = "integer", ["description"] = "Maximum results per page" },
                    ["pageNumber"] = new JObject { ["type"] = "integer", ["description"] = "Page number" }
                },
                Array.Empty<string>()),

            // --- Schedules ---
            CreateTool("retrieve_employee_schedule", "Retrieve schedule details for one or more employees over a date range.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["hyperfindId"] = new JObject { ["type"] = "string", ["description"] = "Hyperfind ID (optional)" },
                    ["personNumbers"] = new JObject { ["type"] = "array", ["description"] = "List of person numbers (optional)", ["items"] = new JObject { ["type"] = "string" } }
                },
                new[] { "startDate", "endDate" }),

            CreateTool("retrieve_schedule", "Retrieve manager-view schedules.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["hyperfindId"] = new JObject { ["type"] = "string", ["description"] = "Hyperfind ID (optional)" },
                    ["locationId"] = new JObject { ["type"] = "string", ["description"] = "Location ID (optional)" }
                },
                new[] { "startDate", "endDate" }),

            CreateTool("retrieve_employee_schedule_changes", "Retrieve schedule change events in a date range.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["hyperfindId"] = new JObject { ["type"] = "string", ["description"] = "Hyperfind ID (optional)" }
                },
                new[] { "startDate", "endDate" }),

            // --- Timecards ---
            CreateTool("retrieve_employee_timecard", "Retrieve the authenticated employee's timecard.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" }
                },
                new[] { "startDate", "endDate" }),

            CreateTool("retrieve_timecards", "Retrieve timecards for one or more employees as a manager.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["hyperfindId"] = new JObject { ["type"] = "string", ["description"] = "Hyperfind ID (optional)" },
                    ["personNumbers"] = new JObject { ["type"] = "array", ["description"] = "Person numbers (optional)", ["items"] = new JObject { ["type"] = "string" } }
                },
                new[] { "startDate", "endDate" }),

            // --- Punches ---
            CreateTool("retrieve_punches", "Retrieve real-time punches for one or more employees.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["personNumbers"] = new JObject { ["type"] = "array", ["description"] = "Person numbers (optional)", ["items"] = new JObject { ["type"] = "string" } }
                },
                new[] { "startDate", "endDate" }),

            CreateTool("bulk_import_punches", "Bulk import punches asynchronously.",
                new JObject
                {
                    ["punches"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Array of punches to import",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["personNumber"] = new JObject { ["type"] = "string" },
                                ["punchDateTime"] = new JObject { ["type"] = "string" },
                                ["punchType"] = new JObject { ["type"] = "string", ["description"] = "IN, OUT, BREAK_START, BREAK_END" },
                                ["transferString"] = new JObject { ["type"] = "string" }
                            }
                        }
                    }
                },
                new[] { "punches" }),

            CreateTool("add_timestamp", "Punch in or out via a timestamp for the authenticated user.",
                new JObject
                {
                    ["punchType"] = new JObject { ["type"] = "string", ["description"] = "IN, OUT, BREAK_START, BREAK_END" },
                    ["transferString"] = new JObject { ["type"] = "string", ["description"] = "Transfer string (optional)" },
                    ["comment"] = new JObject { ["type"] = "string", ["description"] = "Comment (optional)" }
                },
                new[] { "punchType" }),

            CreateTool("retrieve_timestamp", "Retrieve current timestamp state for the authenticated user.",
                new JObject(), Array.Empty<string>()),

            // --- Paycodes ---
            CreateTool("retrieve_paycodes", "Retrieve configured pay codes.",
                new JObject(), Array.Empty<string>()),

            CreateTool("create_pay_code_edit", "Add a pay code edit to an employee timecard.",
                new JObject
                {
                    ["personNumber"] = new JObject { ["type"] = "string", ["description"] = "Person number" },
                    ["effectiveDate"] = new JObject { ["type"] = "string", ["description"] = "Effective date (YYYY-MM-DD)" },
                    ["payCodeName"] = new JObject { ["type"] = "string", ["description"] = "Pay code name" },
                    ["amount"] = new JObject { ["type"] = "number", ["description"] = "Amount" },
                    ["amountType"] = new JObject { ["type"] = "string", ["description"] = "Hours, Money, or Days" },
                    ["comment"] = new JObject { ["type"] = "string", ["description"] = "Comment (optional)" }
                },
                new[] { "personNumber", "effectiveDate", "payCodeName", "amount", "amountType" }),

            CreateTool("bulk_import_pay_code_edits", "Bulk import pay code edits asynchronously.",
                new JObject
                {
                    ["payCodeEdits"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Array of pay code edits",
                        ["items"] = new JObject { ["type"] = "object" }
                    }
                },
                new[] { "payCodeEdits" }),

            // --- Time Off ---
            CreateTool("retrieve_employee_time_off_requests", "Retrieve the authenticated employee's time off requests.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" }
                },
                Array.Empty<string>()),

            CreateTool("retrieve_manager_time_off_requests", "Retrieve time off requests as a manager.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["hyperfindId"] = new JObject { ["type"] = "string", ["description"] = "Hyperfind ID (optional)" },
                    ["status"] = new JObject { ["type"] = "string", ["description"] = "Request status filter (optional)" }
                },
                new[] { "startDate", "endDate" }),

            CreateTool("create_employee_time_off_request", "Submit a new time off request as the authenticated employee.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["payCodeName"] = new JObject { ["type"] = "string", ["description"] = "Pay code name" },
                    ["hours"] = new JObject { ["type"] = "number", ["description"] = "Hours" },
                    ["comment"] = new JObject { ["type"] = "string", ["description"] = "Comment (optional)" }
                },
                new[] { "startDate", "endDate", "payCodeName", "hours" }),

            CreateTool("update_manager_time_off_request", "Approve, reject, or cancel a time off request as a manager.",
                new JObject
                {
                    ["timeOffRequestId"] = new JObject { ["type"] = "string", ["description"] = "Time off request ID" },
                    ["action"] = new JObject { ["type"] = "string", ["description"] = "approve, reject, or cancel" },
                    ["comment"] = new JObject { ["type"] = "string", ["description"] = "Comment (optional)" }
                },
                new[] { "timeOffRequestId", "action" }),

            // --- Accruals ---
            CreateTool("retrieve_accrual_profiles", "Retrieve all accrual profile definitions.",
                new JObject(), Array.Empty<string>()),

            CreateTool("retrieve_accrual_balance", "Retrieve accrual balances for one or more employees on a specific date.",
                new JObject
                {
                    ["asOfDate"] = new JObject { ["type"] = "string", ["description"] = "As of date (YYYY-MM-DD)" },
                    ["personNumbers"] = new JObject { ["type"] = "array", ["description"] = "Person numbers", ["items"] = new JObject { ["type"] = "string" } }
                },
                new[] { "asOfDate", "personNumbers" }),

            // --- Leave ---
            CreateTool("retrieve_leave_cases", "Retrieve all leave cases.",
                new JObject
                {
                    ["pageSize"] = new JObject { ["type"] = "integer", ["description"] = "Maximum results per page" },
                    ["pageNumber"] = new JObject { ["type"] = "integer", ["description"] = "Page number" }
                },
                Array.Empty<string>()),

            CreateTool("retrieve_leave_case_by_id", "Retrieve leave case details by case ID.",
                new JObject
                {
                    ["caseId"] = new JObject { ["type"] = "string", ["description"] = "Leave case ID" }
                },
                new[] { "caseId" }),

            // --- Hyperfinds ---
            CreateTool("retrieve_hyperfind_queries", "Retrieve all hyperfind queries accessible to the authenticated user.",
                new JObject(), Array.Empty<string>()),

            CreateTool("execute_hyperfind", "Execute a hyperfind by ID and return matching person identities.",
                new JObject
                {
                    ["hyperfindId"] = new JObject { ["type"] = "string", ["description"] = "Hyperfind ID" },
                    ["asOfDate"] = new JObject { ["type"] = "string", ["description"] = "As of date (YYYY-MM-DD, optional)" }
                },
                new[] { "hyperfindId" }),

            // --- Notifications ---
            CreateTool("retrieve_notifications", "Retrieve in-app notifications for the authenticated user.",
                new JObject
                {
                    ["pageSize"] = new JObject { ["type"] = "integer", ["description"] = "Maximum results per page" },
                    ["pageNumber"] = new JObject { ["type"] = "integer", ["description"] = "Page number" }
                },
                Array.Empty<string>())
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private JObject CreateTool(string name, string description, JObject properties, string[] required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JArray(required)
            }
        };
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        await LogToAppInsights("MCPToolCall", new
        {
            CorrelationId = correlationId,
            Tool = toolName,
            HasArguments = arguments.HasValues
        });

        try
        {
            var result = await ExecuteToolAsync(toolName, arguments);

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPToolError", new
            {
                CorrelationId = correlationId,
                Tool = toolName,
                ErrorMessage = ex.Message
            });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Tool execution failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    private async Task<JToken> ExecuteToolAsync(string toolName, JObject args)
    {
        switch (toolName)
        {
            // --- Common Resources ---
            case "retrieve_tenant_information":
                return await CallWfmApi("GET", "/api/v1/commons/tenant");

            case "get_current_user":
                return await CallWfmApi("GET", "/api/v1/commons/persons/me");

            case "retrieve_persons":
                return await CallWfmApi("GET", "/api/v1/commons/persons", null,
                    new JObject { ["pageSize"] = args["pageSize"], ["pageNumber"] = args["pageNumber"] });

            case "retrieve_persons_by_criteria":
            {
                var where = new JObject();
                if (args["hyperfindId"] != null)
                {
                    where["hyperfind"] = new JObject
                    {
                        ["id"] = args["hyperfindId"],
                        ["qualifier"] = args["hyperfindQualifier"] ?? "Public"
                    };
                }
                if (args["personNumbers"] is JArray pns && pns.Count > 0)
                {
                    where["personNumbers"] = pns;
                }
                return await CallWfmApi("POST", "/api/v1/commons/persons/extensions/multi_read",
                    new JObject { ["where"] = where });
            }

            case "get_person_by_id":
                return await CallWfmApi("GET",
                    $"/api/v1/commons/persons/{Uri.EscapeDataString(args["personIdentity"].ToString())}");

            case "retrieve_locations":
                return await CallWfmApi("GET", "/api/v1/commons/locations", null,
                    new JObject { ["pageSize"] = args["pageSize"], ["pageNumber"] = args["pageNumber"] });

            case "retrieve_jobs":
                return await CallWfmApi("GET", "/api/v1/commons/jobs", null,
                    new JObject { ["pageSize"] = args["pageSize"], ["pageNumber"] = args["pageNumber"] });

            case "retrieve_cost_centers":
                return await CallWfmApi("GET", "/api/v1/commons/cost_centers", null,
                    new JObject { ["pageSize"] = args["pageSize"], ["pageNumber"] = args["pageNumber"] });

            // --- Schedules ---
            case "retrieve_employee_schedule":
                return await CallWfmApi("POST", "/api/v1/scheduling/employee_schedule/multi_read",
                    BuildScheduleQuery(args));

            case "retrieve_schedule":
                return await CallWfmApi("POST", "/api/v1/scheduling/schedule/multi_read",
                    BuildScheduleQuery(args));

            case "retrieve_employee_schedule_changes":
                return await CallWfmApi("POST", "/api/v1/scheduling/employee_schedule_changes/multi_read",
                    BuildScheduleQuery(args));

            // --- Timecards ---
            case "retrieve_employee_timecard":
                return await CallWfmApi("GET", "/api/v1/timekeeping/timecard/employee", null,
                    new JObject { ["startDate"] = args["startDate"], ["endDate"] = args["endDate"] });

            case "retrieve_timecards":
                return await CallWfmApi("POST", "/api/v1/timekeeping/timecard/multi_read",
                    BuildScheduleQuery(args));

            // --- Punches ---
            case "retrieve_punches":
            {
                var where = new JObject
                {
                    ["dateRange"] = new JObject
                    {
                        ["startDate"] = args["startDate"],
                        ["endDate"] = args["endDate"]
                    }
                };
                if (args["personNumbers"] is JArray pn && pn.Count > 0)
                {
                    var identities = new JArray();
                    foreach (var p in pn)
                        identities.Add(new JObject { ["qualifier"] = "PersonNumber", ["value"] = p });
                    where["personIdentities"] = identities;
                }
                return await CallWfmApi("POST", "/api/v1/timekeeping/punches/multi_read",
                    new JObject { ["where"] = where });
            }

            case "bulk_import_punches":
            {
                var inputPunches = args["punches"] as JArray ?? new JArray();
                var transformed = new JArray();
                foreach (var p in inputPunches)
                {
                    var pObj = p as JObject;
                    if (pObj == null) continue;
                    transformed.Add(new JObject
                    {
                        ["personIdentity"] = new JObject
                        {
                            ["qualifier"] = "PersonNumber",
                            ["value"] = pObj["personNumber"]
                        },
                        ["punchDateTime"] = pObj["punchDateTime"],
                        ["punchType"] = pObj["punchType"],
                        ["transferString"] = pObj["transferString"]
                    });
                }
                return await CallWfmApi("POST", "/api/v1/timekeeping/punches/bulk_import",
                    new JObject { ["punches"] = transformed });
            }

            case "add_timestamp":
            {
                var ts = new JObject
                {
                    ["punchType"] = args["punchType"]
                };
                if (args["transferString"] != null) ts["transferString"] = args["transferString"];
                if (args["comment"] != null) ts["comment"] = args["comment"];
                return await CallWfmApi("POST", "/api/v1/timekeeping/timestamp", ts);
            }

            case "retrieve_timestamp":
                return await CallWfmApi("GET", "/api/v1/timekeeping/timestamp");

            // --- Paycodes ---
            case "retrieve_paycodes":
                return await CallWfmApi("GET", "/api/v1/timekeeping/setup/paycodes");

            case "create_pay_code_edit":
            {
                var pce = new JObject
                {
                    ["personIdentity"] = new JObject
                    {
                        ["qualifier"] = "PersonNumber",
                        ["value"] = args["personNumber"]
                    },
                    ["effectiveDate"] = args["effectiveDate"],
                    ["payCodeName"] = args["payCodeName"],
                    ["amount"] = args["amount"],
                    ["amountType"] = args["amountType"]
                };
                if (args["comment"] != null) pce["comment"] = args["comment"];
                return await CallWfmApi("POST", "/api/v1/timekeeping/pay_code_edits", pce);
            }

            case "bulk_import_pay_code_edits":
                return await CallWfmApi("POST", "/api/v1/timekeeping/pay_code_edits/bulk_import",
                    new JObject { ["payCodeEdits"] = args["payCodeEdits"] });

            // --- Time Off ---
            case "retrieve_employee_time_off_requests":
                return await CallWfmApi("GET", "/api/v1/timeoff/employee", null,
                    new JObject { ["startDate"] = args["startDate"], ["endDate"] = args["endDate"] });

            case "retrieve_manager_time_off_requests":
            {
                var where = new JObject
                {
                    ["dateRange"] = new JObject
                    {
                        ["startDate"] = args["startDate"],
                        ["endDate"] = args["endDate"]
                    }
                };
                if (args["hyperfindId"] != null) where["hyperfindId"] = args["hyperfindId"];
                if (args["status"] != null) where["status"] = args["status"];
                return await CallWfmApi("POST", "/api/v1/timeoff/manager/multi_read",
                    new JObject { ["where"] = where });
            }

            case "create_employee_time_off_request":
            {
                var sub = new JObject
                {
                    ["startDate"] = args["startDate"],
                    ["endDate"] = args["endDate"],
                    ["payCodeName"] = args["payCodeName"],
                    ["hours"] = args["hours"]
                };
                if (args["comment"] != null) sub["comment"] = args["comment"];
                return await CallWfmApi("POST", "/api/v1/timeoff/employee", sub);
            }

            case "update_manager_time_off_request":
            {
                var update = new JObject
                {
                    ["action"] = args["action"]
                };
                if (args["comment"] != null) update["comment"] = args["comment"];
                return await CallWfmApi("PUT",
                    $"/api/v1/timeoff/manager/{Uri.EscapeDataString(args["timeOffRequestId"].ToString())}",
                    update);
            }

            // --- Accruals ---
            case "retrieve_accrual_profiles":
                return await CallWfmApi("GET", "/api/v1/accruals/profiles");

            case "retrieve_accrual_balance":
            {
                var identities = new JArray();
                if (args["personNumbers"] is JArray pn)
                {
                    foreach (var p in pn)
                        identities.Add(new JObject { ["qualifier"] = "PersonNumber", ["value"] = p });
                }
                return await CallWfmApi("POST", "/api/v1/accruals/balances/multi_read",
                    new JObject
                    {
                        ["asOfDate"] = args["asOfDate"],
                        ["personIdentities"] = identities
                    });
            }

            // --- Leave ---
            case "retrieve_leave_cases":
                return await CallWfmApi("GET", "/api/v1/leave/cases", null,
                    new JObject { ["pageSize"] = args["pageSize"], ["pageNumber"] = args["pageNumber"] });

            case "retrieve_leave_case_by_id":
                return await CallWfmApi("GET",
                    $"/api/v1/leave/cases/{Uri.EscapeDataString(args["caseId"].ToString())}");

            // --- Hyperfinds ---
            case "retrieve_hyperfind_queries":
                return await CallWfmApi("GET", "/api/v1/commons/hyperfind");

            case "execute_hyperfind":
            {
                var body = new JObject { ["hyperfindId"] = args["hyperfindId"] };
                if (args["asOfDate"] != null) body["asOfDate"] = args["asOfDate"];
                return await CallWfmApi("POST", "/api/v1/commons/hyperfind/execute", body);
            }

            // --- Notifications ---
            case "retrieve_notifications":
                return await CallWfmApi("GET", "/api/v1/notifications", null,
                    new JObject { ["pageSize"] = args["pageSize"], ["pageNumber"] = args["pageNumber"] });

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private JObject BuildScheduleQuery(JObject args)
    {
        var where = new JObject
        {
            ["dateRange"] = new JObject
            {
                ["startDate"] = args["startDate"],
                ["endDate"] = args["endDate"]
            }
        };
        var employees = new JObject();
        if (args["hyperfindId"] != null) employees["hyperfindId"] = args["hyperfindId"];
        if (args["personNumbers"] is JArray pn && pn.Count > 0)
        {
            var identities = new JArray();
            foreach (var p in pn)
                identities.Add(new JObject { ["qualifier"] = "PersonNumber", ["value"] = p });
            employees["personIdentities"] = identities;
        }
        if (employees.HasValues) where["employees"] = employees;
        if (args["locationId"] != null) where["locationId"] = args["locationId"];
        return new JObject { ["where"] = where };
    }

    #endregion

    #region JSON-RPC Helpers

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    #endregion

    #region Application Insights Logging

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
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
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

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
        }
        catch { } // Suppress telemetry errors
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
        if (string.IsNullOrEmpty(connectionString))
            return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("IngestionEndpoint=".Length);
        return "https://dc.services.visualstudio.com/";
    }

    #endregion
}
