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
                case "GetTableSchema":
                    response = await HandleGetTableSchemaAsync();
                    break;
                default:
                    response = await Context.SendAsync(Context.Request, CancellationToken);
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

    #region Dynamic Schema Handler

    private async Task<HttpResponseMessage> HandleGetTableSchemaAsync()
    {
        // Extract table name from the request path
        var path = Context.Request.RequestUri.AbsolutePath;
        var segments = path.Split('/');
        var tableNameIndex = Array.IndexOf(segments, "table") + 1;
        var tableName = tableNameIndex > 0 && tableNameIndex < segments.Length - 1 
            ? segments[tableNameIndex] 
            : "incident";

        // Query sys_dictionary for table fields
        var baseUri = Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var dictUrl = $"{baseUri}/api/now/table/sys_dictionary?sysparm_query=name={tableName}^internal_type!=collection&sysparm_fields=element,column_label,internal_type,mandatory&sysparm_limit=100";

        var dictRequest = new HttpRequestMessage(HttpMethod.Get, dictUrl);
        if (Context.Request.Headers.Authorization != null)
            dictRequest.Headers.Authorization = Context.Request.Headers.Authorization;
        dictRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var dictResponse = await Context.SendAsync(dictRequest, CancellationToken);
        var dictContent = await dictResponse.Content.ReadAsStringAsync();

        if (!dictResponse.IsSuccessStatusCode)
        {
            return dictResponse;
        }

        var dictResult = JObject.Parse(dictContent);
        var fields = dictResult["result"] as JArray ?? new JArray();

        // Build JSON Schema from sys_dictionary fields
        var properties = new JObject();
        var required = new JArray();

        foreach (var field in fields)
        {
            var element = field["element"]?.ToString();
            var label = field["column_label"]?.ToString() ?? element;
            var internalType = field["internal_type"]?.ToString() ?? "string";
            var mandatory = field["mandatory"]?.ToString() == "true";

            if (string.IsNullOrEmpty(element) || element.StartsWith("sys_"))
                continue;

            var jsonType = MapServiceNowTypeToJsonType(internalType);
            properties[element] = new JObject
            {
                ["type"] = jsonType,
                ["x-ms-summary"] = label,
                ["description"] = label
            };

            if (mandatory)
                required.Add(element);
        }

        // Return the schema response
        var schemaResponse = new JObject
        {
            ["schema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(schemaResponse.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private string MapServiceNowTypeToJsonType(string internalType)
    {
        switch (internalType?.ToLower())
        {
            case "integer":
            case "decimal":
            case "numeric":
            case "float":
                return "number";
            case "boolean":
            case "true_false":
                return "boolean";
            case "glide_date":
            case "glide_date_time":
            case "due_date":
                return "string"; // ISO 8601 format
            default:
                return "string";
        }
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
                ["name"] = "servicenow-zurich-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // Table API Tools
            CreateTool("list_records", "List records from any ServiceNow table. Use for searching incidents, users, changes, problems, or any table.",
                new JObject
                {
                    ["table_name"] = new JObject { ["type"] = "string", ["description"] = "Table name (e.g., incident, sys_user, change_request, problem)" },
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "Encoded query filter (e.g., active=true^priority=1)" },
                    ["fields"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated fields to return" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Max records to return (default 10)" }
                },
                new[] { "table_name" }),

            CreateTool("get_record", "Get a specific record by sys_id from any ServiceNow table.",
                new JObject
                {
                    ["table_name"] = new JObject { ["type"] = "string", ["description"] = "Table name" },
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "Record sys_id" },
                    ["fields"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated fields to return" }
                },
                new[] { "table_name", "sys_id" }),

            CreateTool("create_record", "Create a new record in any ServiceNow table.",
                new JObject
                {
                    ["table_name"] = new JObject { ["type"] = "string", ["description"] = "Table name (e.g., incident, change_request)" },
                    ["data"] = new JObject { ["type"] = "object", ["description"] = "Field values for the new record" }
                },
                new[] { "table_name", "data" }),

            CreateTool("update_record", "Update an existing record in any ServiceNow table.",
                new JObject
                {
                    ["table_name"] = new JObject { ["type"] = "string", ["description"] = "Table name" },
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "Record sys_id" },
                    ["data"] = new JObject { ["type"] = "object", ["description"] = "Field values to update" }
                },
                new[] { "table_name", "sys_id", "data" }),

            // Incident shortcuts
            CreateTool("create_incident", "Create a new incident.",
                new JObject
                {
                    ["short_description"] = new JObject { ["type"] = "string", ["description"] = "Brief summary of the incident" },
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "Detailed description" },
                    ["urgency"] = new JObject { ["type"] = "string", ["description"] = "1=High, 2=Medium, 3=Low" },
                    ["impact"] = new JObject { ["type"] = "string", ["description"] = "1=High, 2=Medium, 3=Low" },
                    ["caller_id"] = new JObject { ["type"] = "string", ["description"] = "Caller user sys_id" },
                    ["assignment_group"] = new JObject { ["type"] = "string", ["description"] = "Assignment group sys_id" }
                },
                new[] { "short_description" }),

            // Knowledge Tools
            CreateTool("search_knowledge", "Search knowledge articles for solutions and documentation.",
                new JObject
                {
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "Search text" },
                    ["knowledge_base"] = new JObject { ["type"] = "string", ["description"] = "Knowledge base sys_id (optional)" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Max articles to return" }
                },
                new[] { "query" }),

            CreateTool("get_knowledge_article", "Get a specific knowledge article by sys_id.",
                new JObject
                {
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "Article sys_id" }
                },
                new[] { "sys_id" }),

            // Change Management Tools
            CreateTool("list_changes", "List change requests with optional filtering.",
                new JObject
                {
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "Query filter" },
                    ["type"] = new JObject { ["type"] = "string", ["description"] = "Change type: normal, standard, emergency" },
                    ["state"] = new JObject { ["type"] = "string", ["description"] = "State filter" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Max records" }
                },
                Array.Empty<string>()),

            CreateTool("create_normal_change", "Create a normal change request.",
                new JObject
                {
                    ["short_description"] = new JObject { ["type"] = "string", ["description"] = "Change summary" },
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "Detailed description" },
                    ["justification"] = new JObject { ["type"] = "string", ["description"] = "Business justification" },
                    ["implementation_plan"] = new JObject { ["type"] = "string", ["description"] = "Implementation steps" },
                    ["backout_plan"] = new JObject { ["type"] = "string", ["description"] = "Rollback plan" },
                    ["assignment_group"] = new JObject { ["type"] = "string", ["description"] = "Assignment group sys_id" },
                    ["start_date"] = new JObject { ["type"] = "string", ["description"] = "Planned start date" },
                    ["end_date"] = new JObject { ["type"] = "string", ["description"] = "Planned end date" }
                },
                new[] { "short_description" }),

            CreateTool("get_change_risk", "Get risk assessment for a change request.",
                new JObject
                {
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "Change request sys_id" }
                },
                new[] { "sys_id" }),

            CreateTool("check_change_conflicts", "Check for scheduling conflicts with other changes.",
                new JObject
                {
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "Change request sys_id" }
                },
                new[] { "sys_id" }),

            // Event Management Tools
            CreateTool("create_event", "Push an event to ServiceNow Event Management.",
                new JObject
                {
                    ["source"] = new JObject { ["type"] = "string", ["description"] = "Source system name" },
                    ["node"] = new JObject { ["type"] = "string", ["description"] = "Node/host name" },
                    ["type"] = new JObject { ["type"] = "string", ["description"] = "Event type" },
                    ["severity"] = new JObject { ["type"] = "integer", ["description"] = "0=Clear, 1=Critical, 2=Major, 3=Minor, 4=Warning, 5=Info" },
                    ["resource"] = new JObject { ["type"] = "string", ["description"] = "Affected resource" },
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "Event description" },
                    ["message_key"] = new JObject { ["type"] = "string", ["description"] = "Deduplication key" }
                },
                new[] { "source", "node", "type", "severity" }),

            CreateTool("list_alerts", "List alerts from Event Management.",
                new JObject
                {
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "Query filter" },
                    ["active"] = new JObject { ["type"] = "boolean", ["description"] = "Only active alerts" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Max alerts to return" }
                },
                Array.Empty<string>()),

            CreateTool("acknowledge_alert", "Acknowledge an alert.",
                new JObject
                {
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "Alert sys_id" }
                },
                new[] { "sys_id" }),

            CreateTool("close_alert", "Close an alert.",
                new JObject
                {
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "Alert sys_id" },
                    ["close_notes"] = new JObject { ["type"] = "string", ["description"] = "Closing notes" }
                },
                new[] { "sys_id" }),

            CreateTool("create_incident_from_alert", "Create an incident from an alert.",
                new JObject
                {
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "Alert sys_id" },
                    ["short_description"] = new JObject { ["type"] = "string", ["description"] = "Override incident summary" },
                    ["assignment_group"] = new JObject { ["type"] = "string", ["description"] = "Assignment group sys_id" }
                },
                new[] { "sys_id" }),

            // CMDB Tools
            CreateTool("list_cis", "List Configuration Items from CMDB.",
                new JObject
                {
                    ["class_name"] = new JObject { ["type"] = "string", ["description"] = "CMDB class (e.g., cmdb_ci_server, cmdb_ci_computer)" },
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "Query filter" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Max CIs to return" }
                },
                new[] { "class_name" }),

            CreateTool("get_ci", "Get a Configuration Item with relationships.",
                new JObject
                {
                    ["class_name"] = new JObject { ["type"] = "string", ["description"] = "CMDB class" },
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "CI sys_id" }
                },
                new[] { "class_name", "sys_id" }),

            // Service Catalog Tools
            CreateTool("list_catalog_items", "List available service catalog items.",
                new JObject
                {
                    ["catalog"] = new JObject { ["type"] = "string", ["description"] = "Catalog sys_id" },
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "Category sys_id" },
                    ["search_text"] = new JObject { ["type"] = "string", ["description"] = "Search text" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Max items" }
                },
                Array.Empty<string>()),

            CreateTool("order_catalog_item", "Order a catalog item.",
                new JObject
                {
                    ["sys_id"] = new JObject { ["type"] = "string", ["description"] = "Catalog item sys_id" },
                    ["quantity"] = new JObject { ["type"] = "integer", ["description"] = "Quantity to order" },
                    ["requested_for"] = new JObject { ["type"] = "string", ["description"] = "User sys_id to order for" },
                    ["variables"] = new JObject { ["type"] = "object", ["description"] = "Item variable values" }
                },
                new[] { "sys_id" }),

            // Email Tool
            CreateTool("send_email", "Send an email through ServiceNow.",
                new JObject
                {
                    ["to"] = new JObject { ["type"] = "string", ["description"] = "Recipient email(s), comma-separated" },
                    ["subject"] = new JObject { ["type"] = "string", ["description"] = "Email subject" },
                    ["body"] = new JObject { ["type"] = "string", ["description"] = "Email body (plain text)" },
                    ["html_body"] = new JObject { ["type"] = "string", ["description"] = "HTML body" },
                    ["cc"] = new JObject { ["type"] = "string", ["description"] = "CC recipients" },
                    ["importance"] = new JObject { ["type"] = "string", ["description"] = "low, normal, high" }
                },
                new[] { "to", "subject", "body" }),

            // Performance Analytics Tools
            CreateTool("list_pa_indicators", "List Performance Analytics indicators.",
                new JObject
                {
                    ["include_scores"] = new JObject { ["type"] = "boolean", ["description"] = "Include recent scores" },
                    ["favorites_only"] = new JObject { ["type"] = "boolean", ["description"] = "Only favorite indicators" }
                },
                Array.Empty<string>()),

            CreateTool("get_indicator_scores", "Get scores for a Performance Analytics indicator.",
                new JObject
                {
                    ["uuid"] = new JObject { ["type"] = "string", ["description"] = "Indicator UUID" },
                    ["breakdown"] = new JObject { ["type"] = "string", ["description"] = "Breakdown UUID" }
                },
                new[] { "uuid" }),

            // User/Group Tools
            CreateTool("list_group_members", "List members of a group.",
                new JObject
                {
                    ["group_sys_id"] = new JObject { ["type"] = "string", ["description"] = "Group sys_id" }
                },
                new[] { "group_sys_id" }),

            CreateTool("add_group_member", "Add a user to a group.",
                new JObject
                {
                    ["group_sys_id"] = new JObject { ["type"] = "string", ["description"] = "Group sys_id" },
                    ["user_sys_id"] = new JObject { ["type"] = "string", ["description"] = "User sys_id to add" }
                },
                new[] { "group_sys_id", "user_sys_id" }),

            // Aggregate/Stats Tool
            CreateTool("get_table_stats", "Get aggregate statistics for a table (count, sum, avg, min, max).",
                new JObject
                {
                    ["table_name"] = new JObject { ["type"] = "string", ["description"] = "Table name" },
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "Query filter" },
                    ["count"] = new JObject { ["type"] = "boolean", ["description"] = "Include count" },
                    ["group_by"] = new JObject { ["type"] = "string", ["description"] = "Fields to group by" },
                    ["avg_fields"] = new JObject { ["type"] = "string", ["description"] = "Fields to average" },
                    ["sum_fields"] = new JObject { ["type"] = "string", ["description"] = "Fields to sum" }
                },
                new[] { "table_name" }),

            // Batch Tool
            CreateTool("batch_requests", "Execute multiple API requests in a single call.",
                new JObject
                {
                    ["requests"] = new JObject 
                    { 
                        ["type"] = "array", 
                        ["description"] = "Array of request objects with id, method, url, and optional body",
                        ["items"] = new JObject { ["type"] = "object" }
                    }
                },
                new[] { "requests" })
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

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject args)
    {
        switch (toolName)
        {
            // Table API
            case "list_records":
                return await CallServiceNowApi("GET", $"/now/table/{args["table_name"]}", BuildQueryString(args, "query", "fields", "limit"));
            case "get_record":
                return await CallServiceNowApi("GET", $"/now/table/{args["table_name"]}/{args["sys_id"]}", BuildQueryString(args, "fields"));
            case "create_record":
                return await CallServiceNowApi("POST", $"/now/table/{args["table_name"]}", null, args["data"] as JObject);
            case "update_record":
                return await CallServiceNowApi("PATCH", $"/now/table/{args["table_name"]}/{args["sys_id"]}", null, args["data"] as JObject);

            // Incident shortcut
            case "create_incident":
                return await CallServiceNowApi("POST", "/now/table/incident", null, args);

            // Knowledge
            case "search_knowledge":
                return await CallServiceNowApi("GET", "/sn_km/knowledge/articles", BuildKnowledgeQuery(args));
            case "get_knowledge_article":
                return await CallServiceNowApi("GET", $"/sn_km/knowledge/articles/{args["sys_id"]}");

            // Change Management
            case "list_changes":
                return await CallServiceNowApi("GET", "/sn_chg_rest/change", BuildQueryString(args, "query", "type", "state", "limit"));
            case "create_normal_change":
                return await CallServiceNowApi("POST", "/sn_chg_rest/change/normal", null, args);
            case "get_change_risk":
                return await CallServiceNowApi("GET", $"/sn_chg_rest/change/{args["sys_id"]}/risk");
            case "check_change_conflicts":
                return await CallServiceNowApi("GET", $"/sn_chg_rest/change/{args["sys_id"]}/conflict");

            // Event Management
            case "create_event":
                return await CallServiceNowApi("POST", "/now/em/event", null, args);
            case "list_alerts":
                return await CallServiceNowApi("GET", "/now/em/alert", BuildQueryString(args, "query", "active", "limit"));
            case "acknowledge_alert":
                return await CallServiceNowApi("POST", $"/now/em/alert/{args["sys_id"]}/acknowledge");
            case "close_alert":
                return await CallServiceNowApi("POST", $"/now/em/alert/{args["sys_id"]}/close", null, new JObject { ["close_notes"] = args["close_notes"] });
            case "create_incident_from_alert":
                return await CallServiceNowApi("POST", $"/now/em/alert/{args["sys_id"]}/incident", null, args);

            // CMDB
            case "list_cis":
                return await CallServiceNowApi("GET", $"/now/cmdb/instance/{args["class_name"]}", BuildQueryString(args, "query", "limit"));
            case "get_ci":
                return await CallServiceNowApi("GET", $"/now/cmdb/instance/{args["class_name"]}/{args["sys_id"]}");

            // Service Catalog
            case "list_catalog_items":
                return await CallServiceNowApi("GET", "/sn_sc/servicecatalog/items", BuildCatalogQuery(args));
            case "order_catalog_item":
                var orderBody = new JObject
                {
                    ["sysparm_quantity"] = args["quantity"] ?? 1,
                    ["sysparm_requested_for"] = args["requested_for"],
                    ["variables"] = args["variables"]
                };
                return await CallServiceNowApi("POST", $"/sn_sc/servicecatalog/items/{args["sys_id"]}/order_now", null, orderBody);

            // Email
            case "send_email":
                return await CallServiceNowApi("POST", "/now/email", null, args);

            // Performance Analytics
            case "list_pa_indicators":
                return await CallServiceNowApi("GET", "/now/pa/indicators", BuildPAQuery(args));
            case "get_indicator_scores":
                return await CallServiceNowApi("GET", $"/now/pa/indicators/{args["uuid"]}/scores", BuildQueryString(args, "breakdown"));

            // User/Group
            case "list_group_members":
                return await CallServiceNowApi("GET", $"/now/v1/group/{args["group_sys_id"]}/member");
            case "add_group_member":
                return await CallServiceNowApi("POST", $"/now/v1/group/{args["group_sys_id"]}/member", null, new JObject { ["user"] = args["user_sys_id"] });

            // Stats
            case "get_table_stats":
                return await CallServiceNowApi("GET", $"/now/stats/{args["table_name"]}", BuildStatsQuery(args));

            // Batch
            case "batch_requests":
                return await CallServiceNowApi("POST", "/now/batch", null, new JObject { ["rest_requests"] = args["requests"] });

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private async Task<JObject> CallServiceNowApi(string method, string path, string queryString = null, JObject body = null)
    {
        var baseUri = Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var fullPath = $"{baseUri}/api{path}";
        if (!string.IsNullOrEmpty(queryString))
            fullPath += "?" + queryString;

        var request = new HttpRequestMessage(new HttpMethod(method), fullPath);
        
        // Copy authorization header
        if (Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await Context.SendAsync(request, CancellationToken);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ServiceNow API returned {(int)response.StatusCode}: {content}");
        }

        return string.IsNullOrEmpty(content) ? new JObject() : JObject.Parse(content);
    }

    #region Query Builders

    private string BuildQueryString(JObject args, params string[] fields)
    {
        var parts = new List<string>();
        foreach (var field in fields)
        {
            var value = args[field];
            if (value != null && !string.IsNullOrEmpty(value.ToString()))
            {
                var paramName = field switch
                {
                    "query" => "sysparm_query",
                    "fields" => "sysparm_fields",
                    "limit" => "sysparm_limit",
                    "offset" => "sysparm_offset",
                    "active" => "active",
                    "type" => "type",
                    "state" => "state",
                    "breakdown" => "sysparm_breakdown",
                    _ => field
                };
                parts.Add($"{paramName}={Uri.EscapeDataString(value.ToString())}");
            }
        }
        // Default limit
        if (!parts.Exists(p => p.Contains("sysparm_limit")))
            parts.Add("sysparm_limit=10");
        return string.Join("&", parts);
    }

    private string BuildKnowledgeQuery(JObject args)
    {
        var parts = new List<string>();
        if (args["query"] != null) parts.Add($"query={Uri.EscapeDataString(args["query"].ToString())}");
        if (args["knowledge_base"] != null) parts.Add($"knowledge_base={args["knowledge_base"]}");
        if (args["limit"] != null) parts.Add($"limit={args["limit"]}");
        else parts.Add("limit=10");
        return string.Join("&", parts);
    }

    private string BuildCatalogQuery(JObject args)
    {
        var parts = new List<string>();
        if (args["catalog"] != null) parts.Add($"sysparm_catalog={args["catalog"]}");
        if (args["category"] != null) parts.Add($"sysparm_category={args["category"]}");
        if (args["search_text"] != null) parts.Add($"sysparm_text={Uri.EscapeDataString(args["search_text"].ToString())}");
        if (args["limit"] != null) parts.Add($"sysparm_limit={args["limit"]}");
        else parts.Add("sysparm_limit=10");
        return string.Join("&", parts);
    }

    private string BuildPAQuery(JObject args)
    {
        var parts = new List<string>();
        if (args["include_scores"]?.ToObject<bool>() == true) parts.Add("sysparm_include_scores=true");
        if (args["favorites_only"]?.ToObject<bool>() == true) parts.Add("sysparm_favorites_only=true");
        return string.Join("&", parts);
    }

    private string BuildStatsQuery(JObject args)
    {
        var parts = new List<string>();
        if (args["query"] != null) parts.Add($"sysparm_query={Uri.EscapeDataString(args["query"].ToString())}");
        if (args["count"]?.ToObject<bool>() == true) parts.Add("sysparm_count=true");
        if (args["group_by"] != null) parts.Add($"sysparm_group_by={args["group_by"]}");
        if (args["avg_fields"] != null) parts.Add($"sysparm_avg_fields={args["avg_fields"]}");
        if (args["sum_fields"] != null) parts.Add($"sysparm_sum_fields={args["sum_fields"]}");
        return string.Join("&", parts);
    }

    #endregion

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
                return; // Telemetry disabled

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
