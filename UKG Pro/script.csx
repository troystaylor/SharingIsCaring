using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights configuration - set your connection string to enable telemetry
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

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
                    response = await ForwardToUkg();
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

    #region UKG API Forwarding

    private async Task<HttpResponseMessage> ForwardToUkg()
    {
        var username = (string)this.Context.ConnectionParameters["username"];
        var password = (string)this.Context.ConnectionParameters["password"];
        var apiKey = (string)this.Context.ConnectionParameters["api_key"];

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        this.Context.Request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        this.Context.Request.Headers.TryAddWithoutValidation("US-Customer-Api-Key", apiKey);

        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<JToken> CallUkgApi(string method, string path, JObject body = null)
    {
        var username = (string)this.Context.ConnectionParameters["username"];
        var password = (string)this.Context.ConnectionParameters["password"];
        var apiKey = (string)this.Context.ConnectionParameters["api_key"];

        var baseUri = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var url = $"{baseUri}{path}";
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        request.Headers.TryAddWithoutValidation("US-Customer-Api-Key", apiKey);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await Context.SendAsync(request, CancellationToken);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"UKG API returned {(int)response.StatusCode}: {content}");

        if (string.IsNullOrEmpty(content))
            return new JObject();

        if (content.TrimStart().StartsWith("["))
            return JArray.Parse(content);

        return JObject.Parse(content);
    }

    private string BuildQueryString(JObject args, params string[] paramNames)
    {
        var parts = new List<string>();
        foreach (var name in paramNames)
        {
            var val = args[name]?.ToString();
            if (!string.IsNullOrEmpty(val))
                parts.Add($"{name}={Uri.EscapeDataString(val)}");
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
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
                ["name"] = "ukg-pro-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // --- Configuration ---
            CreateTool("get_company_details", "Get company configuration details.",
                new JObject(),
                Array.Empty<string>()),

            CreateTool("get_jobs", "List all job codes with titles and descriptions.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_locations", "List all company locations.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_org_levels", "List all organization levels.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_earnings", "List all earnings codes.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_positions", "List all positions.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            // --- Personnel ---
            CreateTool("get_employees", "Get person details for all employees with pagination.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_employee", "Get person details for a specific employee by ID.",
                new JObject
                {
                    ["employeeId"] = new JObject { ["type"] = "string", ["description"] = "The UKG employee ID" }
                },
                new[] { "employeeId" }),

            CreateTool("lookup_employee_id", "Look up employee IDs by company ID, employee number, email, or last name.",
                new JObject
                {
                    ["companyId"] = new JObject { ["type"] = "string", ["description"] = "Company ID" },
                    ["employeeNumber"] = new JObject { ["type"] = "string", ["description"] = "Employee number" },
                    ["emailAddress"] = new JObject { ["type"] = "string", ["description"] = "Email address" },
                    ["lastName"] = new JObject { ["type"] = "string", ["description"] = "Last name" }
                },
                Array.Empty<string>()),

            CreateTool("get_employment_details", "Get employment details for all employees.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_compensation", "Get compensation details for all employees or a specific employee.",
                new JObject
                {
                    ["companyId"] = new JObject { ["type"] = "string", ["description"] = "Company ID (required for single employee lookup)" },
                    ["employeeId"] = new JObject { ["type"] = "string", ["description"] = "Employee ID (omit for all employees)" },
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_demographics", "Get employee demographic details.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_employee_changes", "Get employees with changes in a date range. Useful for incremental sync.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                new[] { "startDate", "endDate" }),

            CreateTool("get_pto_plans", "Get PTO plan balances for employees.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_supervisors", "Get supervisor details and reporting relationships.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_contacts", "Get employee emergency contacts.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_deductions", "Get employee deduction details.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_job_history", "Get employee job history records.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_education", "Get employee education records.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            // --- Payroll ---
            CreateTool("get_direct_deposit", "Get employee direct deposit details.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_company_pay_statements", "Get pay statement summaries for a company or pay group in a date range.",
                new JObject
                {
                    ["companyId"] = new JObject { ["type"] = "string", ["description"] = "Company ID" },
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["payGroup"] = new JObject { ["type"] = "string", ["description"] = "Pay group (optional)" }
                },
                new[] { "companyId", "startDate", "endDate" }),

            CreateTool("get_employee_pay_statements", "Get pay statements for a specific employee in a date range.",
                new JObject
                {
                    ["employeeId"] = new JObject { ["type"] = "string", ["description"] = "Employee ID" },
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" }
                },
                new[] { "employeeId", "startDate", "endDate" }),

            CreateTool("get_earnings_history", "Get earnings history records.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            // --- Time & Attendance ---
            CreateTool("get_clock_transactions", "Get employee clock in/out transactions.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                new[] { "startDate", "endDate" }),

            CreateTool("get_work_summaries", "Get employee work hour summaries.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                new[] { "startDate", "endDate" }),

            CreateTool("get_time_off_requests", "Get employee time off requests.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                new[] { "startDate", "endDate" }),

            CreateTool("get_uta_employees", "Get employees configured in Time & Attendance.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            // --- Security & Audit ---
            CreateTool("get_roles", "List all security roles.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_user_profiles", "Get user profile details.",
                new JObject
                {
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                Array.Empty<string>()),

            CreateTool("get_audit_details", "Get audit trail details for a date range.",
                new JObject
                {
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "End date (YYYY-MM-DD)" },
                    ["page"] = new JObject { ["type"] = "integer", ["description"] = "Page number" },
                    ["per_page"] = new JObject { ["type"] = "integer", ["description"] = "Results per page" }
                },
                new[] { "startDate", "endDate" })
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
            // Configuration
            case "get_company_details":
                return await CallUkgApi("GET", "/configuration/v1/company-details");

            case "get_jobs":
                return await CallUkgApi("GET", "/configuration/v1/jobs" + BuildQueryString(args, "page", "per_page"));

            case "get_locations":
                return await CallUkgApi("GET", "/configuration/v1/locations" + BuildQueryString(args, "page", "per_page"));

            case "get_org_levels":
                return await CallUkgApi("GET", "/configuration/v1/org-levels" + BuildQueryString(args, "page", "per_page"));

            case "get_earnings":
                return await CallUkgApi("GET", "/configuration/v1/earnings" + BuildQueryString(args, "page", "per_page"));

            case "get_positions":
                return await CallUkgApi("GET", "/configuration/v1/positions" + BuildQueryString(args, "page", "per_page"));

            // Personnel
            case "get_employees":
                return await CallUkgApi("GET", "/personnel/v1/person-details" + BuildQueryString(args, "page", "per_page"));

            case "get_employee":
                return await CallUkgApi("GET", $"/personnel/v1/person-details/{Uri.EscapeDataString(args["employeeId"].ToString())}");

            case "lookup_employee_id":
                var lookupBody = new JObject();
                if (args["companyId"] != null) lookupBody["companyId"] = args["companyId"];
                if (args["employeeNumber"] != null) lookupBody["employeeNumber"] = args["employeeNumber"];
                if (args["emailAddress"] != null) lookupBody["emailAddress"] = args["emailAddress"];
                if (args["lastName"] != null) lookupBody["lastName"] = args["lastName"];
                return await CallUkgApi("POST", "/personnel/v1/employee-ids", lookupBody);

            case "get_employment_details":
                return await CallUkgApi("GET", "/personnel/v1/employment-details" + BuildQueryString(args, "page", "per_page"));

            case "get_compensation":
                var companyId = args["companyId"]?.ToString();
                var employeeId = args["employeeId"]?.ToString();
                if (!string.IsNullOrEmpty(companyId) && !string.IsNullOrEmpty(employeeId))
                    return await CallUkgApi("GET", $"/personnel/v1/companies/{Uri.EscapeDataString(companyId)}/employees/{Uri.EscapeDataString(employeeId)}/compensation-details");
                return await CallUkgApi("GET", "/personnel/v1/compensation-details" + BuildQueryString(args, "page", "per_page"));

            case "get_demographics":
                return await CallUkgApi("GET", "/personnel/v1/employee-demographic-details" + BuildQueryString(args, "page", "per_page"));

            case "get_employee_changes":
                return await CallUkgApi("GET", "/personnel/v1/employee-changes" + BuildQueryString(args, "startDate", "endDate", "page", "per_page"));

            case "get_pto_plans":
                return await CallUkgApi("GET", "/personnel/v1/pto-plans" + BuildQueryString(args, "page", "per_page"));

            case "get_supervisors":
                return await CallUkgApi("GET", "/personnel/v1/supervisor-details" + BuildQueryString(args, "page", "per_page"));

            case "get_contacts":
                return await CallUkgApi("GET", "/personnel/v1/contacts" + BuildQueryString(args, "page", "per_page"));

            case "get_deductions":
                return await CallUkgApi("GET", "/personnel/v1/emp-deductions" + BuildQueryString(args, "page", "per_page"));

            case "get_job_history":
                return await CallUkgApi("GET", "/personnel/v1/employee-job-history-details" + BuildQueryString(args, "page", "per_page"));

            case "get_education":
                return await CallUkgApi("GET", "/personnel/v1/employee-education" + BuildQueryString(args, "page", "per_page"));

            // Payroll
            case "get_direct_deposit":
                return await CallUkgApi("GET", "/payroll/v1/direct-deposit" + BuildQueryString(args, "page", "per_page"));

            case "get_company_pay_statements":
                var compPayBody = new JObject
                {
                    ["companyId"] = args["companyId"],
                    ["startDate"] = args["startDate"],
                    ["endDate"] = args["endDate"]
                };
                if (args["payGroup"] != null) compPayBody["payGroup"] = args["payGroup"];
                return await CallUkgApi("POST", "/payroll/v1/companies/pay-statements-summary", compPayBody);

            case "get_employee_pay_statements":
                var empPayBody = new JObject
                {
                    ["employeeId"] = args["employeeId"],
                    ["startDate"] = args["startDate"],
                    ["endDate"] = args["endDate"]
                };
                return await CallUkgApi("POST", "/payroll/v1/employees/pay-statements", empPayBody);

            case "get_earnings_history":
                return await CallUkgApi("GET", "/payroll/v1/earnings-history" + BuildQueryString(args, "page", "per_page"));

            // Time & Attendance
            case "get_clock_transactions":
                return await CallUkgApi("GET", "/ta/api/v1/time/clock-transactions" + BuildQueryString(args, "startDate", "endDate", "page", "per_page"));

            case "get_work_summaries":
                return await CallUkgApi("GET", "/ta/api/v1/time/work-summaries" + BuildQueryString(args, "startDate", "endDate", "page", "per_page"));

            case "get_time_off_requests":
                return await CallUkgApi("GET", "/ta/api/v1/time-off-requests" + BuildQueryString(args, "startDate", "endDate", "page", "per_page"));

            case "get_uta_employees":
                return await CallUkgApi("GET", "/ta/api/v1/employees" + BuildQueryString(args, "page", "per_page"));

            // Security & Audit
            case "get_roles":
                return await CallUkgApi("GET", "/configuration/v1/roles" + BuildQueryString(args, "page", "per_page"));

            case "get_user_profiles":
                return await CallUkgApi("GET", "/personnel/v1/user-profile-details" + BuildQueryString(args, "page", "per_page"));

            case "get_audit_details":
                return await CallUkgApi("GET", "/personnel/v1/audit-details" + BuildQueryString(args, "startDate", "endDate", "page", "per_page"));

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
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