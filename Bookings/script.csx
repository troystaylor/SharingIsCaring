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
    // Get from: Azure Portal → Application Insights → Overview → Connection String
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    
    private static readonly string SERVER_NAME = "microsoft-bookings-mcp";
    private static readonly string SERVER_VERSION = "1.0.0";
    private static readonly string DEFAULT_PROTOCOL_VERSION = "2025-12-01";
    private static bool _isInitialized = false;

    // Tool definitions will be added in subsequent updates
    private static readonly JArray AVAILABLE_TOOLS = new JArray();

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            await LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = operationId });

            HttpResponseMessage response;

            // Route MCP requests
            if (operationId == "InvokeMCP")
            {
                response = await HandleMCPRequestAsync(correlationId).ConfigureAwait(false);
            }
            else
            {
                // Route REST API requests
                response = await HandleRESTRequestAsync(operationId).ConfigureAwait(false);
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
                    return CreateSuccess(new JObject(), id);
                case "tools/list":
                    return HandleToolsList(id);
                case "tools/call":
                    return await HandleToolsCallAsync(@params, id, correlationId).ConfigureAwait(false);
                default:
                    // Log unknown methods to help debug
                    await LogToAppInsights("MCPUnknownMethod", new { CorrelationId = correlationId, Method = method ?? "null" });
                    return CreateError(id, -32601, "Method not found", method ?? "");
            }
        }
        catch (JsonException ex)
        {
            await LogToAppInsights("MCPParseError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateError(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateError(null, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(JObject @params, JToken id)
    {
        _isInitialized = true;
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
                ["title"] = "Microsoft Bookings MCP",
                ["description"] = "Model Context Protocol tools for Microsoft Bookings via Microsoft Graph"
            }
        };
        return CreateSuccess(result, id);
    }

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        // Note: No initialization check - Power Platform connectors are stateless
        // Each request is independent, so we always return tools
        return CreateSuccess(new JObject { ["tools"] = GetToolDefinitions() }, id);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject @params, JToken id, string correlationId)
    {
        // Note: No initialization check - Power Platform connectors are stateless
        var toolName = @params?["name"]?.ToString();
        var args = @params?["arguments"] as JObject ?? new JObject();
        if (string.IsNullOrWhiteSpace(toolName))
            return CreateError(id, -32602, "Tool name required", "name parameter is required");

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
            // Business tools
            CreateToolDef("listBookingBusinesses", "List all booking businesses in the tenant",
                new JObject { ["top"] = IntProp("Number of items to return"), ["filter"] = StrProp("OData filter") }),
            CreateToolDef("getBookingBusiness", "Get a specific booking business by ID",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true) }, new[] { "bookingBusinessId" }),
            CreateToolDef("createBookingBusiness", "Create a new booking business",
                new JObject { ["displayName"] = StrProp("Business name", true), ["email"] = StrProp("Email"), ["phone"] = StrProp("Phone"), ["businessType"] = StrProp("Type of business"), ["webSiteUrl"] = StrProp("Website URL") }, new[] { "displayName" }),
            CreateToolDef("updateBookingBusiness", "Update a booking business",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["displayName"] = StrProp("Business name"), ["email"] = StrProp("Email"), ["phone"] = StrProp("Phone") }, new[] { "bookingBusinessId" }),
            CreateToolDef("deleteBookingBusiness", "Delete a booking business",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true) }, new[] { "bookingBusinessId" }),
            CreateToolDef("publishBookingBusiness", "Publish booking page to customers",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true) }, new[] { "bookingBusinessId" }),
            CreateToolDef("unpublishBookingBusiness", "Unpublish booking page",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true) }, new[] { "bookingBusinessId" }),

            // Service tools
            CreateToolDef("listServices", "List services for a booking business",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["top"] = IntProp("Number of items") }, new[] { "bookingBusinessId" }),
            CreateToolDef("getService", "Get a specific service",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["serviceId"] = StrProp("Service ID", true) }, new[] { "bookingBusinessId", "serviceId" }),
            CreateToolDef("createService", "Create a new service",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["displayName"] = StrProp("Service name", true), ["description"] = StrProp("Description"), ["defaultDuration"] = StrProp("Duration ISO 8601 (e.g., PT1H)"), ["defaultPrice"] = NumProp("Price"), ["isLocationOnline"] = BoolProp("Online meeting") }, new[] { "bookingBusinessId", "displayName" }),
            CreateToolDef("updateService", "Update a service",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["serviceId"] = StrProp("Service ID", true), ["displayName"] = StrProp("Service name"), ["description"] = StrProp("Description") }, new[] { "bookingBusinessId", "serviceId" }),
            CreateToolDef("deleteService", "Delete a service",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["serviceId"] = StrProp("Service ID", true) }, new[] { "bookingBusinessId", "serviceId" }),

            // Staff tools
            CreateToolDef("listStaffMembers", "List staff members for a booking business",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["top"] = IntProp("Number of items") }, new[] { "bookingBusinessId" }),
            CreateToolDef("getStaffMember", "Get a specific staff member",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["staffMemberId"] = StrProp("Staff member ID", true) }, new[] { "bookingBusinessId", "staffMemberId" }),
            CreateToolDef("createStaffMember", "Create a new staff member",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["displayName"] = StrProp("Name", true), ["emailAddress"] = StrProp("Email", true), ["role"] = StrProp("Role: administrator, viewer, externalGuest, scheduler, teamMember") }, new[] { "bookingBusinessId", "displayName", "emailAddress" }),
            CreateToolDef("updateStaffMember", "Update a staff member",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["staffMemberId"] = StrProp("Staff member ID", true), ["displayName"] = StrProp("Name"), ["role"] = StrProp("Role") }, new[] { "bookingBusinessId", "staffMemberId" }),
            CreateToolDef("deleteStaffMember", "Delete a staff member",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["staffMemberId"] = StrProp("Staff member ID", true) }, new[] { "bookingBusinessId", "staffMemberId" }),
            CreateToolDef("getStaffAvailability", "Get staff availability in date range",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["staffIds"] = ArrProp("Staff member IDs", true), ["startDateTime"] = StrProp("Start (ISO 8601)", true), ["endDateTime"] = StrProp("End (ISO 8601)", true), ["timeZone"] = StrProp("Timezone (default UTC)") }, new[] { "bookingBusinessId", "staffIds", "startDateTime", "endDateTime" }),

            // Customer tools
            CreateToolDef("listCustomers", "List customers for a booking business",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["top"] = IntProp("Number of items") }, new[] { "bookingBusinessId" }),
            CreateToolDef("getCustomer", "Get a specific customer",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["customerId"] = StrProp("Customer ID", true) }, new[] { "bookingBusinessId", "customerId" }),
            CreateToolDef("createCustomer", "Create a new customer",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["displayName"] = StrProp("Name", true), ["emailAddress"] = StrProp("Email", true), ["phone"] = StrProp("Phone number") }, new[] { "bookingBusinessId", "displayName", "emailAddress" }),
            CreateToolDef("updateCustomer", "Update a customer",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["customerId"] = StrProp("Customer ID", true), ["displayName"] = StrProp("Name"), ["emailAddress"] = StrProp("Email") }, new[] { "bookingBusinessId", "customerId" }),
            CreateToolDef("deleteCustomer", "Delete a customer",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["customerId"] = StrProp("Customer ID", true) }, new[] { "bookingBusinessId", "customerId" }),

            // Appointment tools
            CreateToolDef("listAppointments", "List appointments for a booking business",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["top"] = IntProp("Number of items"), ["filter"] = StrProp("OData filter") }, new[] { "bookingBusinessId" }),
            CreateToolDef("getAppointment", "Get a specific appointment",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["appointmentId"] = StrProp("Appointment ID", true) }, new[] { "bookingBusinessId", "appointmentId" }),
            CreateToolDef("createAppointment", "Create a new appointment",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["serviceId"] = StrProp("Service ID", true), ["startDateTime"] = StrProp("Start (ISO 8601)", true), ["endDateTime"] = StrProp("End (ISO 8601)", true), ["timeZone"] = StrProp("Timezone (default UTC)"), ["customerEmailAddress"] = StrProp("Customer email"), ["customerName"] = StrProp("Customer name"), ["staffMemberIds"] = ArrProp("Staff IDs"), ["isLocationOnline"] = BoolProp("Online meeting"), ["serviceNotes"] = StrProp("Notes") }, new[] { "bookingBusinessId", "serviceId", "startDateTime", "endDateTime" }),
            CreateToolDef("updateAppointment", "Update an appointment",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["appointmentId"] = StrProp("Appointment ID", true), ["startDateTime"] = StrProp("Start (ISO 8601)"), ["endDateTime"] = StrProp("End (ISO 8601)"), ["serviceNotes"] = StrProp("Notes") }, new[] { "bookingBusinessId", "appointmentId" }),
            CreateToolDef("deleteAppointment", "Delete an appointment",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["appointmentId"] = StrProp("Appointment ID", true) }, new[] { "bookingBusinessId", "appointmentId" }),
            CreateToolDef("cancelAppointment", "Cancel an appointment with message",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["appointmentId"] = StrProp("Appointment ID", true), ["cancellationMessage"] = StrProp("Message to customer", true) }, new[] { "bookingBusinessId", "appointmentId", "cancellationMessage" }),
            CreateToolDef("getCalendarView", "Get appointments in a date range",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["start"] = StrProp("Start date (ISO 8601)", true), ["end"] = StrProp("End date (ISO 8601)", true) }, new[] { "bookingBusinessId", "start", "end" }),

            // Custom Questions tools
            CreateToolDef("listCustomQuestions", "List custom questions for a booking business",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["top"] = IntProp("Number of items") }, new[] { "bookingBusinessId" }),
            CreateToolDef("getCustomQuestion", "Get a specific custom question",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["customQuestionId"] = StrProp("Custom question ID", true) }, new[] { "bookingBusinessId", "customQuestionId" }),
            CreateToolDef("createCustomQuestion", "Create a new custom question",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["displayName"] = StrProp("Question text", true), ["answerInputType"] = StrProp("Answer type: text, radioButton"), ["answerOptions"] = ArrProp("Answer options for radioButton type") }, new[] { "bookingBusinessId", "displayName" }),
            CreateToolDef("updateCustomQuestion", "Update a custom question",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["customQuestionId"] = StrProp("Custom question ID", true), ["displayName"] = StrProp("Question text"), ["answerInputType"] = StrProp("Answer type"), ["answerOptions"] = ArrProp("Answer options") }, new[] { "bookingBusinessId", "customQuestionId" }),
            CreateToolDef("deleteCustomQuestion", "Delete a custom question",
                new JObject { ["bookingBusinessId"] = StrProp("Booking business ID", true), ["customQuestionId"] = StrProp("Custom question ID", true) }, new[] { "bookingBusinessId", "customQuestionId" }),

            // Currency tools
            CreateToolDef("listBookingCurrencies", "List available booking currencies",
                new JObject { ["top"] = IntProp("Number of items") }),
            CreateToolDef("getBookingCurrency", "Get a specific currency by ID",
                new JObject { ["currencyId"] = StrProp("Currency ID (e.g., USD, EUR)", true) }, new[] { "currencyId" })
        };
    }

    // Helper methods for tool definition
    private JObject CreateToolDef(string name, string desc, JObject props, string[] required = null)
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = props };
        if (required != null && required.Length > 0) schema["required"] = new JArray(required);
        return new JObject { ["name"] = name, ["description"] = desc, ["inputSchema"] = schema };
    }
    private JObject StrProp(string desc, bool req = false) => new JObject { ["type"] = "string", ["description"] = desc };
    private JObject IntProp(string desc) => new JObject { ["type"] = "integer", ["description"] = desc };
    private JObject NumProp(string desc) => new JObject { ["type"] = "number", ["description"] = desc };
    private JObject BoolProp(string desc) => new JObject { ["type"] = "boolean", ["description"] = desc };
    private JObject ArrProp(string desc, bool req = false) => new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = desc };

    #endregion

    #region Tool Execution

    private async Task<HttpResponseMessage> ExecuteToolAsync(string toolName, JObject args, JToken id)
    {
        switch (toolName)
        {
            // Business tools
            case "listBookingBusinesses": return await CallGraphAsync("GET", "/solutions/bookingBusinesses", args, id, new[] { "top", "filter" });
            case "getBookingBusiness": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}", null, id);
            case "createBookingBusiness": return await CallGraphAsync("POST", "/solutions/bookingBusinesses", BuildBody(args, "displayName", "email", "phone", "businessType", "webSiteUrl"), id);
            case "updateBookingBusiness": return await CallGraphAsync("PATCH", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}", BuildBody(args, "displayName", "email", "phone", "businessType", "webSiteUrl"), id);
            case "deleteBookingBusiness": return await CallGraphAsync("DELETE", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}", null, id);
            case "publishBookingBusiness": return await CallGraphAsync("POST", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/publish", null, id);
            case "unpublishBookingBusiness": return await CallGraphAsync("POST", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/unpublish", null, id);

            // Service tools
            case "listServices": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/services", args, id, new[] { "top" });
            case "getService": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/services/{Arg(args, "serviceId")}", null, id);
            case "createService": return await CallGraphAsync("POST", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/services", BuildServiceBody(args), id);
            case "updateService": return await CallGraphAsync("PATCH", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/services/{Arg(args, "serviceId")}", BuildBody(args, "displayName", "description", "defaultDuration", "defaultPrice", "isLocationOnline"), id);
            case "deleteService": return await CallGraphAsync("DELETE", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/services/{Arg(args, "serviceId")}", null, id);

            // Staff tools
            case "listStaffMembers": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/staffMembers", args, id, new[] { "top" });
            case "getStaffMember": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/staffMembers/{Arg(args, "staffMemberId")}", null, id);
            case "createStaffMember": return await CallGraphAsync("POST", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/staffMembers", BuildBody(args, "displayName", "emailAddress", "role", "timeZone", "useBusinessHours"), id);
            case "updateStaffMember": return await CallGraphAsync("PATCH", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/staffMembers/{Arg(args, "staffMemberId")}", BuildBody(args, "displayName", "emailAddress", "role"), id);
            case "deleteStaffMember": return await CallGraphAsync("DELETE", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/staffMembers/{Arg(args, "staffMemberId")}", null, id);
            case "getStaffAvailability": return await CallGraphAsync("POST", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/getStaffAvailability", BuildStaffAvailabilityBody(args), id);

            // Customer tools
            case "listCustomers": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customers", args, id, new[] { "top" });
            case "getCustomer": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customers/{Arg(args, "customerId")}", null, id);
            case "createCustomer": return await CallGraphAsync("POST", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customers", BuildCustomerBody(args), id);
            case "updateCustomer": return await CallGraphAsync("PATCH", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customers/{Arg(args, "customerId")}", BuildBody(args, "displayName", "emailAddress"), id);
            case "deleteCustomer": return await CallGraphAsync("DELETE", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customers/{Arg(args, "customerId")}", null, id);

            // Appointment tools
            case "listAppointments": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/appointments", args, id, new[] { "top", "filter" });
            case "getAppointment": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/appointments/{Arg(args, "appointmentId")}", null, id);
            case "createAppointment": return await CallGraphAsync("POST", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/appointments", BuildAppointmentBody(args), id);
            case "updateAppointment": return await CallGraphAsync("PATCH", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/appointments/{Arg(args, "appointmentId")}", BuildAppointmentUpdateBody(args), id);
            case "deleteAppointment": return await CallGraphAsync("DELETE", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/appointments/{Arg(args, "appointmentId")}", null, id);
            case "cancelAppointment": return await CallGraphAsync("POST", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/appointments/{Arg(args, "appointmentId")}/cancel", new JObject { ["cancellationMessage"] = Arg(args, "cancellationMessage") }, id);
            case "getCalendarView": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/calendarView?start={Uri.EscapeDataString(Arg(args, "start"))}&end={Uri.EscapeDataString(Arg(args, "end"))}", null, id);

            // Custom Questions tools
            case "listCustomQuestions": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customQuestions", args, id, new[] { "top" });
            case "getCustomQuestion": return await CallGraphAsync("GET", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customQuestions/{Arg(args, "customQuestionId")}", null, id);
            case "createCustomQuestion": return await CallGraphAsync("POST", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customQuestions", BuildCustomQuestionBody(args), id);
            case "updateCustomQuestion": return await CallGraphAsync("PATCH", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customQuestions/{Arg(args, "customQuestionId")}", BuildBody(args, "displayName", "answerInputType", "answerOptions"), id);
            case "deleteCustomQuestion": return await CallGraphAsync("DELETE", $"/solutions/bookingBusinesses/{Arg(args, "bookingBusinessId")}/customQuestions/{Arg(args, "customQuestionId")}", null, id);

            // Currency tools
            case "listBookingCurrencies": return await CallGraphAsync("GET", "/solutions/bookingCurrencies", args, id, new[] { "top" });
            case "getBookingCurrency": return await CallGraphAsync("GET", $"/solutions/bookingCurrencies/{Arg(args, "currencyId")}", null, id);

            default: return CreateError(id, -32601, "Unknown tool", toolName);
        }
    }

    #endregion

    #region REST Operation Handlers

    private async Task<HttpResponseMessage> HandleRESTRequestAsync(string operationId)
    {
        var path = this.Context.Request.RequestUri.AbsolutePath;
        var query = this.Context.Request.RequestUri.Query;
        var method = this.Context.Request.Method;
        var v = GraphVersion();
        var url = $"https://graph.microsoft.com/{v}{path}{query}";

        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Accept", "application/json");

        if (method == HttpMethod.Post || method == HttpMethod.Put || method.Method == "PATCH")
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(body))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        return response;
    }

    #endregion

    #region Graph API Helpers

    private string GraphVersion() => "v1.0";

    private string Arg(JObject args, string key)
    {
        var val = args?[key]?.ToString();
        if (string.IsNullOrWhiteSpace(val)) throw new ArgumentException($"{key} is required");
        return val;
    }

    private string ArgOpt(JObject args, string key) => args?[key]?.ToString();

    private async Task<HttpResponseMessage> CallGraphAsync(string method, string path, JObject body, JToken id, string[] queryParams = null)
    {
        var v = GraphVersion();
        var url = new StringBuilder($"https://graph.microsoft.com/{v}{path}");

        // Add query parameters for GET requests
        if (queryParams != null && body != null)
        {
            var qp = new List<string>();
            foreach (var p in queryParams)
            {
                var val = body[p]?.ToString();
                if (!string.IsNullOrEmpty(val))
                {
                    var paramName = p == "top" ? "$top" : (p == "filter" ? "$filter" : p);
                    qp.Add($"{paramName}={Uri.EscapeDataString(val)}");
                }
            }
            if (qp.Count > 0 && !path.Contains("?")) url.Append("?" + string.Join("&", qp));
        }

        var httpMethod = method switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => new HttpMethod("PATCH"),
            "PUT" => HttpMethod.Put,
            _ => HttpMethod.Get
        };

        var request = new HttpRequestMessage(httpMethod, url.ToString());
        request.Headers.Add("Accept", "application/json");

        if (body != null && method != "GET" && method != "DELETE")
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var textOut = string.IsNullOrEmpty(content) ? $"Status: {(int)response.StatusCode} {response.StatusCode}" : content;
        return CreateToolResult(textOut, !response.IsSuccessStatusCode, id);
    }

    #endregion

    #region Body Builders

    private JObject BuildBody(JObject args, params string[] fields)
    {
        var body = new JObject();
        foreach (var f in fields)
        {
            var val = args?[f];
            if (val != null && val.Type != JTokenType.Null) body[f] = val;
        }
        return body;
    }

    private JObject BuildServiceBody(JObject args)
    {
        var body = BuildBody(args, "displayName", "description", "defaultPrice", "isLocationOnline");
        var duration = ArgOpt(args, "defaultDuration");
        if (!string.IsNullOrEmpty(duration)) body["defaultDuration"] = duration;
        return body;
    }

    private JObject BuildCustomerBody(JObject args)
    {
        var body = BuildBody(args, "displayName", "emailAddress");
        var phone = ArgOpt(args, "phone");
        if (!string.IsNullOrEmpty(phone))
        {
            body["phones"] = new JArray { new JObject { ["number"] = phone, ["type"] = "mobile" } };
        }
        return body;
    }

    private JObject BuildStaffAvailabilityBody(JObject args)
    {
        var tz = ArgOpt(args, "timeZone") ?? "UTC";
        return new JObject
        {
            ["staffIds"] = args["staffIds"],
            ["startDateTime"] = new JObject { ["dateTime"] = Arg(args, "startDateTime"), ["timeZone"] = tz },
            ["endDateTime"] = new JObject { ["dateTime"] = Arg(args, "endDateTime"), ["timeZone"] = tz }
        };
    }

    private JObject BuildAppointmentBody(JObject args)
    {
        var tz = ArgOpt(args, "timeZone") ?? "UTC";
        var body = new JObject
        {
            ["serviceId"] = Arg(args, "serviceId"),
            ["startDateTime"] = new JObject { ["dateTime"] = Arg(args, "startDateTime"), ["timeZone"] = tz },
            ["endDateTime"] = new JObject { ["dateTime"] = Arg(args, "endDateTime"), ["timeZone"] = tz }
        };

        var email = ArgOpt(args, "customerEmailAddress");
        var name = ArgOpt(args, "customerName");
        var staff = args?["staffMemberIds"] as JArray;
        var online = args?["isLocationOnline"];
        var notes = ArgOpt(args, "serviceNotes");

        if (!string.IsNullOrEmpty(email)) body["customerEmailAddress"] = email;
        if (!string.IsNullOrEmpty(name)) body["customerName"] = name;
        if (staff != null) body["staffMemberIds"] = staff;
        if (online != null) body["isLocationOnline"] = online;
        if (!string.IsNullOrEmpty(notes)) body["serviceNotes"] = notes;

        return body;
    }

    private JObject BuildAppointmentUpdateBody(JObject args)
    {
        var body = new JObject();
        var tz = ArgOpt(args, "timeZone") ?? "UTC";

        var start = ArgOpt(args, "startDateTime");
        var end = ArgOpt(args, "endDateTime");
        var notes = ArgOpt(args, "serviceNotes");

        if (!string.IsNullOrEmpty(start)) body["startDateTime"] = new JObject { ["dateTime"] = start, ["timeZone"] = tz };
        if (!string.IsNullOrEmpty(end)) body["endDateTime"] = new JObject { ["dateTime"] = end, ["timeZone"] = tz };
        if (!string.IsNullOrEmpty(notes)) body["serviceNotes"] = notes;

        return body;
    }

    private JObject BuildCustomQuestionBody(JObject args)
    {
        var body = new JObject();
        if (args["displayName"] != null) body["displayName"] = args["displayName"];
        if (args["answerInputType"] != null) body["answerInputType"] = args["answerInputType"];
        if (args["answerOptions"] != null) body["answerOptions"] = args["answerOptions"];
        return body;
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
                return; // Telemetry disabled

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

    #region Response Helpers

    private HttpResponseMessage CreateToolResult(string text, bool isError, JToken id)
    {
        return CreateSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError
        }, id);
    }

    private HttpResponseMessage CreateSuccess(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        return resp;
    }

    private HttpResponseMessage CreateError(JToken id, int code, string message, string data = null)
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
