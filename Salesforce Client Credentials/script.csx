using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights configuration - set your connection string to enable telemetry
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // Stores the last telemetry error for diagnostics (null = no error)
    private string _lastTelemetryError = null;

    // Salesforce API version
    private const string API_VERSION = "v66.0";

    // Default language for Knowledge Articles and Search Suggestions API calls
    private const string DEFAULT_LANGUAGE = "en-US";

    // Cached access token and instance URL for the current request
    private string _cachedAccessToken = null;
    private string _cachedInstanceUrl = null;

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

            // Acquire OAuth token via client_credentials flow
            var accessToken = await GetAccessTokenAsync(correlationId);

            HttpResponseMessage response;

            switch (Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpAsync(correlationId);
                    break;
                case "GetObjectSchema":
                    response = await HandleGetObjectSchemaAsync();
                    break;
                default:
                    // Passthrough: rebuild request against the real Salesforce instance with Bearer token
                    var instanceUrl = GetInstanceUrl();
                    var originalPath = Context.Request.RequestUri.PathAndQuery;
                    var targetUri = $"https://{instanceUrl}{originalPath}";

                    var forwardRequest = new HttpRequestMessage(Context.Request.Method, targetUri);
                    forwardRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    forwardRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    if (Context.Request.Content != null)
                    {
                        var bodyContent = await Context.Request.Content.ReadAsStringAsync();
                        if (!string.IsNullOrEmpty(bodyContent))
                            forwardRequest.Content = new StringContent(bodyContent, Encoding.UTF8, "application/json");
                    }

                    response = await Context.SendAsync(forwardRequest, CancellationToken);
                    break;
            }

            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                StatusCode = (int)response.StatusCode,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });

            if (_lastTelemetryError != null)
            {
                response.Headers.TryAddWithoutValidation("X-Telemetry-Debug", _lastTelemetryError);
            }

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

    private async Task<HttpResponseMessage> HandleGetObjectSchemaAsync()
    {
        // Extract sObject name from the request path
        var path = Context.Request.RequestUri.AbsolutePath;
        var segments = path.Split('/');
        var sobjectIndex = Array.IndexOf(segments, "sobjects") + 1;
        var sObject = sobjectIndex > 0 && sobjectIndex < segments.Length
            ? segments[sobjectIndex]
            : "Account";

        // Call describe endpoint for the sObject using client_credentials token
        var instanceUrl = GetInstanceUrl();
        var accessToken = await GetAccessTokenAsync("schema");
        var describeUrl = $"https://{instanceUrl}/services/data/{API_VERSION}/sobjects/{sObject}/describe";

        var describeRequest = new HttpRequestMessage(HttpMethod.Get, describeUrl);
        describeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        describeRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var describeResponse = await Context.SendAsync(describeRequest, CancellationToken);
        var describeContent = await describeResponse.Content.ReadAsStringAsync();

        if (!describeResponse.IsSuccessStatusCode)
        {
            await LogToAppInsights("DescribeError", new
            {
                SObject = sObject,
                StatusCode = (int)describeResponse.StatusCode,
                SalesforceError = TruncateForLog(describeContent, 500)
            });
            return describeResponse;
        }

        var describeResult = JObject.Parse(describeContent);
        var fields = describeResult["fields"] as JArray ?? new JArray();

        // Build JSON Schema from Salesforce field metadata
        var properties = new JObject();
        var required = new JArray();

        foreach (var field in fields)
        {
            var name = field["name"]?.ToString();
            var label = field["label"]?.ToString() ?? name;
            var sfType = field["type"]?.ToString() ?? "string";
            var createable = field["createable"]?.ToObject<bool>() ?? false;
            var nillable = field["nillable"]?.ToObject<bool>() ?? true;

            // Only include createable fields (skip formula, auto-number, etc.)
            if (!createable || string.IsNullOrEmpty(name))
                continue;

            var jsonType = MapSalesforceTypeToJsonType(sfType);
            properties[name] = new JObject
            {
                ["type"] = jsonType,
                ["x-ms-summary"] = label,
                ["description"] = label
            };

            // Add picklist values if available
            var picklistValues = field["picklistValues"] as JArray;
            if (picklistValues != null && picklistValues.Count > 0)
            {
                var enumValues = new JArray();
                foreach (var pv in picklistValues)
                {
                    if (pv["active"]?.ToObject<bool>() == true)
                        enumValues.Add(pv["value"]?.ToString());
                }
                if (enumValues.Count > 0)
                    properties[name]["enum"] = enumValues;
            }

            if (!nillable)
                required.Add(name);
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

    private string MapSalesforceTypeToJsonType(string sfType)
    {
        switch (sfType?.ToLower())
        {
            case "int":
            case "integer":
            case "long":
                return "integer";
            case "double":
            case "currency":
            case "percent":
                return "number";
            case "boolean":
                return "boolean";
            case "date":
            case "datetime":
            case "time":
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
                return HandleResourcesList(requestId);

            case "resources/read":
                return HandleResourcesRead(@params, requestId);

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
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "salesforce-cc",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // Query Tools
            CreateTool("query", "Execute a SOQL query to retrieve records. Use for searching and filtering data.",
                new JObject
                {
                    ["soql"] = new JObject { ["type"] = "string", ["description"] = "SOQL query (e.g., SELECT Id, Name FROM Account WHERE Industry = 'Technology' LIMIT 10)" }
                },
                new[] { "soql" }),

            CreateTool("search", "Execute a SOSL search across multiple objects for full-text search.",
                new JObject
                {
                    ["sosl"] = new JObject { ["type"] = "string", ["description"] = "SOSL query (e.g., FIND {Acme} IN ALL FIELDS RETURNING Account(Id, Name), Contact(Id, Name))" }
                },
                new[] { "sosl" }),

            // Record Tools
            CreateTool("get_record", "Get a single Salesforce record by ID.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name (e.g., Account, Contact, Lead, Opportunity)" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Record ID (15 or 18 character)" },
                    ["fields"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated fields to return (optional)" }
                },
                new[] { "object", "id" }),

            CreateTool("create_record", "Create a new record in Salesforce.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name (e.g., Account, Contact, Lead)" },
                    ["data"] = new JObject { ["type"] = "object", ["description"] = "Field values for the new record" }
                },
                new[] { "object", "data" }),

            CreateTool("update_record", "Update an existing Salesforce record.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Record ID" },
                    ["data"] = new JObject { ["type"] = "object", ["description"] = "Field values to update" }
                },
                new[] { "object", "id", "data" }),

            CreateTool("delete_record", "Delete a Salesforce record.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Record ID" }
                },
                new[] { "object", "id" }),

            // Metadata Tools
            CreateTool("list_objects", "List all available sObjects in the org.",
                new JObject(),
                Array.Empty<string>()),

            CreateTool("describe_object", "Get detailed metadata about an sObject including fields and relationships.",
                new JObject
                {
                    ["object"] = new JObject { ["type"] = "string", ["description"] = "sObject API name (e.g., Account, Contact)" }
                },
                new[] { "object" }),

            CreateTool("get_limits", "Get the org's API limits and current usage.",
                new JObject(),
                Array.Empty<string>()),

            // Analytics Tools
            CreateTool("list_reports", "List available reports in the org.",
                new JObject(),
                Array.Empty<string>()),

            CreateTool("run_report", "Execute a Salesforce report and return results.",
                new JObject
                {
                    ["report_id"] = new JObject { ["type"] = "string", ["description"] = "Report ID" }
                },
                new[] { "report_id" }),

            CreateTool("list_dashboards", "List available dashboards in the org.",
                new JObject(),
                Array.Empty<string>()),

            // Chatter Tools
            CreateTool("post_to_chatter", "Post a message to Chatter.",
                new JObject
                {
                    ["subject_id"] = new JObject { ["type"] = "string", ["description"] = "User ID or Record ID to post to" },
                    ["message"] = new JObject { ["type"] = "string", ["description"] = "Message text" }
                },
                new[] { "subject_id", "message" }),

            CreateTool("get_chatter_feed", "Get Chatter feed for news, company, or a record.",
                new JObject
                {
                    ["feed_type"] = new JObject { ["type"] = "string", ["description"] = "Feed type: news, company, record, user-profile" }
                },
                new[] { "feed_type" }),

            // Composite Tool
            CreateTool("composite", "Execute multiple API operations in a single request.",
                new JObject
                {
                    ["all_or_none"] = new JObject { ["type"] = "boolean", ["description"] = "Roll back all on any failure" },
                    ["requests"] = new JObject 
                    { 
                        ["type"] = "array", 
                        ["description"] = "Array of requests with method, url, referenceId, and optional body",
                        ["items"] = new JObject { ["type"] = "object" }
                    }
                },
                new[] { "requests" }),

            // Knowledge Tools
            CreateTool("list_knowledge_articles", "List Knowledge articles visible to the current user. Supports search and pagination.",
                new JObject
                {
                    ["q"] = new JObject { ["type"] = "string", ["description"] = "Search term for articles (optional)" },
                    ["channel"] = new JObject { ["type"] = "string", ["description"] = "Channel context: App, Pkb, Csp, Prm (optional)" },
                    ["page_size"] = new JObject { ["type"] = "integer", ["description"] = "Number of articles per page, max 100 (optional)" }
                },
                Array.Empty<string>()),

            CreateTool("get_knowledge_article", "Get the full content and metadata of a Knowledge article by ID.",
                new JObject
                {
                    ["article_id"] = new JObject { ["type"] = "string", ["description"] = "Knowledge Article ID" },
                    ["channel"] = new JObject { ["type"] = "string", ["description"] = "Channel context: App, Pkb, Csp, Prm (optional)" }
                },
                new[] { "article_id" }),

            CreateTool("create_knowledge_article", "Create a new Knowledge article draft.",
                new JObject
                {
                    ["article_type"] = new JObject { ["type"] = "string", ["description"] = "API name of the article type (e.g., FAQ__kav, Knowledge__kav)" },
                    ["title"] = new JObject { ["type"] = "string", ["description"] = "Article title" },
                    ["url_name"] = new JObject { ["type"] = "string", ["description"] = "URL-friendly name for the article" },
                    ["fields"] = new JObject { ["type"] = "object", ["description"] = "Additional article fields as key-value pairs (optional)" }
                },
                new[] { "article_type", "title", "url_name" }),

            CreateTool("update_knowledge_article", "Update a Knowledge article. Can update title, fields, or manage lifecycle.",
                new JObject
                {
                    ["article_id"] = new JObject { ["type"] = "string", ["description"] = "Knowledge Article ID" },
                    ["title"] = new JObject { ["type"] = "string", ["description"] = "New article title (optional)" },
                    ["fields"] = new JObject { ["type"] = "object", ["description"] = "Article fields to update (optional)" }
                },
                new[] { "article_id" }),

            CreateTool("delete_knowledge_article", "Delete a Knowledge article.",
                new JObject
                {
                    ["article_id"] = new JObject { ["type"] = "string", ["description"] = "Knowledge Article ID" }
                },
                new[] { "article_id" }),

            // Search Suggestion Tools
            CreateTool("search_suggestions", "Get search query suggestions based on what other users have searched in Knowledge. Synonym-aware.",
                new JObject
                {
                    ["q"] = new JObject { ["type"] = "string", ["description"] = "Search query string (minimum 3 characters)" },
                    ["language"] = new JObject { ["type"] = "string", ["description"] = "Language code (e.g., en_US)" },
                    ["channel"] = new JObject { ["type"] = "string", ["description"] = "Channel context: AllChannels, App, Pkb, Csp, Prm (optional)" }
                },
                new[] { "q", "language" }),

            CreateTool("suggest_article_titles", "Get Knowledge article titles matching a search query.",
                new JObject
                {
                    ["q"] = new JObject { ["type"] = "string", ["description"] = "Search query string" },
                    ["language"] = new JObject { ["type"] = "string", ["description"] = "Article language (e.g., en_US)" },
                    ["publish_status"] = new JObject { ["type"] = "string", ["description"] = "Article status: Draft, Online, or Archived" },
                    ["article_type"] = new JObject { ["type"] = "string", ["description"] = "Article type API name to filter by (optional)" }
                },
                new[] { "q", "language", "publish_status" }),

            // Synonym Tools
            CreateTool("list_synonym_groups", "List all search synonym groups in the org.",
                new JObject(),
                Array.Empty<string>()),

            CreateTool("get_synonym_group", "Get details of a specific synonym group by ID.",
                new JObject
                {
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Synonym Group ID" }
                },
                new[] { "id" }),

            CreateTool("create_synonym_group", "Create a new search synonym group.",
                new JObject
                {
                    ["group_name"] = new JObject { ["type"] = "string", ["description"] = "Name of the synonym group" },
                    ["synonyms"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated list of synonym terms (e.g., 'CRM, customer relationship management')" }
                },
                new[] { "group_name", "synonyms" }),

            CreateTool("update_synonym_group", "Update an existing synonym group.",
                new JObject
                {
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Synonym Group ID" },
                    ["group_name"] = new JObject { ["type"] = "string", ["description"] = "New group name (optional)" },
                    ["synonyms"] = new JObject { ["type"] = "string", ["description"] = "Updated comma-separated synonym terms (optional)" }
                },
                new[] { "id" }),

            CreateTool("delete_synonym_group", "Delete a search synonym group.",
                new JObject
                {
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Synonym Group ID" }
                },
                new[] { "id" }),

            // Case Creation Tool
            CreateTool("create_case", "Create a new support Case in Salesforce. Returns CaseNumber and Id. Use this instead of create_record when creating Cases.",
                new JObject
                {
                    ["subject"] = new JObject { ["type"] = "string", ["description"] = "Case subject / short summary of the issue" },
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "Detailed description including environment, steps to reproduce, and what was already tried" },
                    ["priority"] = new JObject { ["type"] = "string", ["description"] = "Case priority: Critical, High, Medium, or Low" },
                    ["contact_email"] = new JObject { ["type"] = "string", ["description"] = "Contact email to associate with the case (optional — looked up automatically)" },
                    ["type"] = new JObject { ["type"] = "string", ["description"] = "Case type (e.g., Problem, Feature Request, Question). Optional." },
                    ["origin"] = new JObject { ["type"] = "string", ["description"] = "Case origin channel. Defaults to Chat if omitted." }
                },
                new[] { "subject", "description", "priority" }),

            // Deflection Tracking Tool
            CreateTool("log_interaction", "Log a Copilot agent interaction for deflection tracking and metrics. Call this at the end of every triage conversation.",
                new JObject
                {
                    ["outcome"] = new JObject { ["type"] = "string", ["description"] = "Interaction outcome: Deflected, Case Created, Abandoned, or Escalated" },
                    ["severity"] = new JObject { ["type"] = "string", ["description"] = "Severity classification: Sev 1 - Critical, Sev 2 - High, Sev 3 - Medium, or Sev 4 - Low" },
                    ["topic"] = new JObject { ["type"] = "string", ["description"] = "Primary topic or category of the user's question" },
                    ["summary"] = new JObject { ["type"] = "string", ["description"] = "Brief summary of the interaction and resolution" },
                    ["kb_articles_shown"] = new JObject { ["type"] = "integer", ["description"] = "Number of KB articles presented during self-service (0 if none)" },
                    ["case_id"] = new JObject { ["type"] = "string", ["description"] = "Salesforce Case ID if a case was created (optional)" },
                    ["channel"] = new JObject { ["type"] = "string", ["description"] = "Channel: Service Console, Customer Portal, or Documentation Site" },
                    ["conversation_id"] = new JObject { ["type"] = "string", ["description"] = "Direct Line conversation ID (optional)" },
                    ["user_email"] = new JObject { ["type"] = "string", ["description"] = "Email of the user (optional)" }
                },
                new[] { "outcome", "summary" }),

            // Phase 3 — Autonomous Actions

            // Case Timeline (for summary, closure summary, and email drafting)
            CreateTool("get_case_timeline", "Retrieve a Case with its full timeline: CaseComments, EmailMessages, and FeedItems. Use this before generating a case summary, closure summary, or response email.",
                new JObject
                {
                    ["case_id"] = new JObject { ["type"] = "string", ["description"] = "Salesforce Case ID (18-char)" }
                },
                new[] { "case_id" }),

            // Send Case Email
            CreateTool("send_case_email", "Send or draft an outbound email on a Case. Creates an EmailMessage record linked to the case.",
                new JObject
                {
                    ["case_id"] = new JObject { ["type"] = "string", ["description"] = "Case ID to associate the email with" },
                    ["to_address"] = new JObject { ["type"] = "string", ["description"] = "Recipient email address" },
                    ["subject"] = new JObject { ["type"] = "string", ["description"] = "Email subject line" },
                    ["body"] = new JObject { ["type"] = "string", ["description"] = "Email body (HTML supported)" },
                    ["status"] = new JObject { ["type"] = "string", ["description"] = "Email status: Draft or Sent. Defaults to Draft so the agent can review before sending." }
                },
                new[] { "case_id", "to_address", "subject", "body" }),

            // KB Suggestion for Case
            CreateTool("suggest_kb_for_case", "Search Knowledge articles matching a case's subject and description. Returns ranked article matches for the agent to recommend or attach.",
                new JObject
                {
                    ["case_id"] = new JObject { ["type"] = "string", ["description"] = "Case ID to find matching KB articles for" },
                    ["search_terms"] = new JObject { ["type"] = "string", ["description"] = "Additional search keywords to refine results (optional)" }
                },
                new[] { "case_id" }),

            // Draft KB Article from Case
            CreateTool("draft_kb_article", "Create a Knowledge article draft pre-populated with data from a resolved case. Useful when no existing KB covers the issue.",
                new JObject
                {
                    ["case_id"] = new JObject { ["type"] = "string", ["description"] = "Case ID to base the article on" },
                    ["article_type"] = new JObject { ["type"] = "string", ["description"] = "Knowledge article type API name (e.g., Knowledge__kav). Defaults to Knowledge__kav." },
                    ["title"] = new JObject { ["type"] = "string", ["description"] = "Article title" },
                    ["url_name"] = new JObject { ["type"] = "string", ["description"] = "URL-friendly slug for the article" },
                    ["summary"] = new JObject { ["type"] = "string", ["description"] = "Article summary / short description" },
                    ["content"] = new JObject { ["type"] = "string", ["description"] = "Full article body content (HTML or plain text)" }
                },
                new[] { "case_id", "title", "url_name", "content" }),

            // Case Categorization
            CreateTool("categorize_case", "Update a Case with categorization fields: Type, Reason, product area, and root cause. Use after analyzing case content.",
                new JObject
                {
                    ["case_id"] = new JObject { ["type"] = "string", ["description"] = "Case ID to categorize" },
                    ["type"] = new JObject { ["type"] = "string", ["description"] = "Case type: Problem, Feature Request, or Question" },
                    ["reason"] = new JObject { ["type"] = "string", ["description"] = "Case reason (e.g., Installation, Performance, Compatibility, New Feature)" },
                    ["subject"] = new JObject { ["type"] = "string", ["description"] = "Updated subject with standardized naming (optional)" },
                    ["internal_comments"] = new JObject { ["type"] = "string", ["description"] = "Internal comment explaining the categorization rationale" }
                },
                new[] { "case_id", "type", "reason" }),

            // --- Phase 5: Reporting & Analytics ---

            // Case Trends
            CreateTool("get_case_trends", "Get case volume trends grouped by a specified field over a time period. Returns counts and percentages for trend analysis and manager dashboards.",
                new JObject
                {
                    ["group_by"] = new JObject { ["type"] = "string", ["description"] = "Field to group by: Type, Reason, Priority, Status, Origin, or a custom field API name" },
                    ["days"] = new JObject { ["type"] = "integer", ["description"] = "Lookback period in days (default: 30)" },
                    ["min_count"] = new JObject { ["type"] = "integer", ["description"] = "Minimum count to include in results (default: 1)" }
                },
                new[] { "group_by" }),

            // Deflection Metrics
            CreateTool("get_deflection_metrics", "Get deflection metrics from Copilot_Interaction__c records. Returns outcome counts, deflection rates, and optional grouping by topic, channel, or severity.",
                new JObject
                {
                    ["days"] = new JObject { ["type"] = "integer", ["description"] = "Lookback period in days (default: 30)" },
                    ["group_by"] = new JObject { ["type"] = "string", ["description"] = "Optional grouping field: Topic__c, Channel__c, Severity__c, or none (default: none)" }
                },
                new string[] { }),

            // SLA Compliance
            CreateTool("get_sla_compliance", "Get SLA compliance metrics for cases. Compares first response time against SLA thresholds by priority. Returns compliance percentages and breached case details.",
                new JObject
                {
                    ["days"] = new JObject { ["type"] = "integer", ["description"] = "Lookback period in days (default: 30)" },
                    ["priority"] = new JObject { ["type"] = "string", ["description"] = "Optional filter by priority: Critical, High, Medium, or Low" }
                },
                new string[] { })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private HttpResponseMessage HandleResourcesList(JToken requestId)
    {
        var resources = new JArray
        {
            new JObject
            {
                ["uri"] = "salesforce://reference/soql",
                ["name"] = "SOQL Query Reference",
                ["description"] = "Comprehensive guide to Salesforce Object Query Language (SOQL) including syntax, operators, functions, and examples",
                ["mimeType"] = "text/plain"
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = resources });
    }

    private HttpResponseMessage HandleResourcesRead(JObject @params, JToken requestId)
    {
        var uri = @params["uri"]?.ToString();

        if (uri == "salesforce://reference/soql")
        {
            var content = GetSoqlReference();
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = "text/plain",
                        ["text"] = content
                    }
                }
            });
        }

        return CreateJsonRpcErrorResponse(requestId, -32002, "Resource not found", uri);
    }

    private string GetSoqlReference()
    {
        return @"# SOQL (Salesforce Object Query Language) Reference

## Basic Syntax
SELECT field1, field2, ... FROM ObjectName [WHERE conditions] [ORDER BY field] [LIMIT n] [OFFSET n]

## SELECT Clause
- SELECT Id, Name, Industry FROM Account
- SELECT COUNT() FROM Account (aggregate only)
- SELECT COUNT(Id), Industry FROM Account GROUP BY Industry
- SELECT * is NOT supported - must specify fields

## FROM Clause
Single object: FROM Account
With relationship (parent): FROM Contact (queries Contact, can traverse to Account)
With relationship (child): SELECT Id, (SELECT Id FROM Contacts) FROM Account

## WHERE Clause Operators

### Comparison Operators
=       Equal: WHERE Industry = 'Technology'
!=      Not equal: WHERE Industry != 'Technology'
<       Less than: WHERE Amount < 10000
<=      Less or equal: WHERE Amount <= 10000
>       Greater than: WHERE Amount > 10000
>=      Greater or equal: WHERE Amount >= 10000

### String Operators
LIKE    Pattern match: WHERE Name LIKE 'Acme%'
        % = zero or more chars, _ = single char
        WHERE Email LIKE '%@gmail.com'

### Set Operators
IN          WHERE Industry IN ('Technology', 'Finance', 'Healthcare')
NOT IN      WHERE Industry NOT IN ('Agriculture', 'Energy')
INCLUDES    For multi-select picklists: WHERE Interests__c INCLUDES ('Golf', 'Tennis')
EXCLUDES    WHERE Interests__c EXCLUDES ('Golf')

### Logical Operators
AND     WHERE Industry = 'Technology' AND AnnualRevenue > 1000000
OR      WHERE Industry = 'Technology' OR Industry = 'Finance'
NOT     WHERE NOT Industry = 'Technology'
        Parentheses for grouping: WHERE (Industry = 'Tech' OR Industry = 'Finance') AND AnnualRevenue > 1000000

### Null Checks
= null      WHERE Email = null
!= null     WHERE Email != null

## Date Literals (Use instead of hardcoded dates)

### Relative Today
TODAY                   Today
YESTERDAY               Yesterday
TOMORROW                Tomorrow

### Relative Days
LAST_N_DAYS:n           Last n days: WHERE CreatedDate = LAST_N_DAYS:30
NEXT_N_DAYS:n           Next n days: WHERE CloseDate = NEXT_N_DAYS:7

### Week
THIS_WEEK               Current week (Sun-Sat)
LAST_WEEK               Previous week
NEXT_WEEK               Next week
LAST_N_WEEKS:n          Last n weeks
NEXT_N_WEEKS:n          Next n weeks

### Month
THIS_MONTH              Current month
LAST_MONTH              Previous month
NEXT_MONTH              Next month
LAST_N_MONTHS:n         Last n months: WHERE CreatedDate = LAST_N_MONTHS:3
NEXT_N_MONTHS:n         Next n months

### Quarter
THIS_QUARTER            Current quarter
LAST_QUARTER            Previous quarter
NEXT_QUARTER            Next quarter
THIS_FISCAL_QUARTER     Current fiscal quarter
LAST_FISCAL_QUARTER     Previous fiscal quarter
NEXT_FISCAL_QUARTER     Next fiscal quarter
LAST_N_QUARTERS:n       Last n quarters
NEXT_N_QUARTERS:n       Next n quarters

### Year
THIS_YEAR               Current year
LAST_YEAR               Previous year
NEXT_YEAR               Next year
THIS_FISCAL_YEAR        Current fiscal year
LAST_FISCAL_YEAR        Previous fiscal year
NEXT_FISCAL_YEAR        Next fiscal year
LAST_N_YEARS:n          Last n years
NEXT_N_YEARS:n          Next n years

## Aggregate Functions
COUNT()     Total records: SELECT COUNT() FROM Account
COUNT(field) Non-null count: SELECT COUNT(Industry) FROM Account
SUM(field)  Sum: SELECT SUM(Amount) FROM Opportunity
AVG(field)  Average: SELECT AVG(Amount) FROM Opportunity
MIN(field)  Minimum: SELECT MIN(Amount) FROM Opportunity
MAX(field)  Maximum: SELECT MAX(Amount) FROM Opportunity

### GROUP BY
SELECT Industry, COUNT(Id) FROM Account GROUP BY Industry
SELECT Industry, SUM(AnnualRevenue) FROM Account GROUP BY Industry

### HAVING (filter aggregates)
SELECT Industry, COUNT(Id) cnt FROM Account GROUP BY Industry HAVING COUNT(Id) > 10

## ORDER BY
ORDER BY Name               Ascending (default)
ORDER BY Name ASC           Ascending explicit
ORDER BY Name DESC          Descending
ORDER BY Name ASC NULLS FIRST   Nulls first
ORDER BY Name ASC NULLS LAST    Nulls last
ORDER BY CreatedDate DESC, Name ASC   Multiple fields

## LIMIT and OFFSET
LIMIT n         Return max n records: LIMIT 100
OFFSET n        Skip first n records: OFFSET 10
                Max OFFSET: 2000
                Combine: LIMIT 10 OFFSET 20 (records 21-30)

## Relationship Queries

### Parent-to-Child (Subquery)
SELECT Id, Name, (SELECT Id, LastName FROM Contacts) FROM Account
- Child relationship name is usually plural (Contacts, Opportunities, Cases)
- Check object metadata for exact relationship name

### Child-to-Parent (Dot Notation)
SELECT Id, LastName, Account.Name, Account.Industry FROM Contact
- Use relationship name (usually same as parent object)
- Can traverse up to 5 levels: Account.Owner.Manager.Name

## Common Standard Objects

### Account
Key fields: Id, Name, Industry, Type, Website, Phone, BillingCity, BillingState, BillingCountry, AnnualRevenue, NumberOfEmployees, OwnerId, CreatedDate
Relationships: Owner (User), Contacts, Opportunities, Cases

### Contact
Key fields: Id, FirstName, LastName, Name, Email, Phone, Title, Department, AccountId, OwnerId, MailingCity, MailingState
Relationships: Account, Owner (User), Cases, Opportunities (via OpportunityContactRole)

### Lead
Key fields: Id, FirstName, LastName, Name, Email, Phone, Company, Title, Status, LeadSource, Industry, ConvertedAccountId, ConvertedContactId, ConvertedOpportunityId
Relationships: Owner (User), ConvertedAccount, ConvertedContact, ConvertedOpportunity

### Opportunity
Key fields: Id, Name, Amount, StageName, CloseDate, Probability, Type, LeadSource, AccountId, OwnerId, IsClosed, IsWon
Relationships: Account, Owner (User), OpportunityLineItems, OpportunityContactRoles

### Case
Key fields: Id, CaseNumber, Subject, Description, Status, Priority, Origin, Type, AccountId, ContactId, OwnerId, IsClosed
Relationships: Account, Contact, Owner (User), CaseComments

### Task
Key fields: Id, Subject, Description, Status, Priority, ActivityDate, WhoId (Contact/Lead), WhatId (Account/Opportunity/etc), OwnerId, IsClosed
Relationships: Who (Contact or Lead), What (any object), Owner

### Event
Key fields: Id, Subject, Description, StartDateTime, EndDateTime, IsAllDayEvent, WhoId, WhatId, OwnerId
Relationships: Who, What, Owner

### User
Key fields: Id, Username, Email, FirstName, LastName, Name, Title, Department, IsActive, ProfileId, UserRoleId, ManagerId
Relationships: Profile, UserRole, Manager

## Common Query Examples

### Find accounts by industry
SELECT Id, Name, Industry, AnnualRevenue FROM Account WHERE Industry = 'Technology' LIMIT 100

### Find contacts with email at a company
SELECT Id, FirstName, LastName, Email FROM Contact WHERE Account.Name = 'Acme Corp' AND Email != null

### Find open opportunities closing this month
SELECT Id, Name, Amount, StageName, CloseDate, Account.Name FROM Opportunity WHERE IsClosed = false AND CloseDate = THIS_MONTH ORDER BY CloseDate ASC

### Find high-value opportunities
SELECT Id, Name, Amount, StageName, Account.Name FROM Opportunity WHERE Amount > 100000 AND StageName != 'Closed Lost' ORDER BY Amount DESC LIMIT 20

### Count opportunities by stage
SELECT StageName, COUNT(Id), SUM(Amount) FROM Opportunity WHERE IsClosed = false GROUP BY StageName

### Find recent leads
SELECT Id, Name, Company, Email, Status, CreatedDate FROM Lead WHERE CreatedDate = LAST_N_DAYS:7 ORDER BY CreatedDate DESC

### Find accounts with contacts
SELECT Id, Name, (SELECT Id, Name, Email FROM Contacts WHERE Email != null) FROM Account WHERE Industry = 'Technology' LIMIT 50

### Find contacts and their account info
SELECT Id, Name, Email, Account.Name, Account.Industry FROM Contact WHERE Account.Industry = 'Technology' LIMIT 100

### Search across name fields
SELECT Id, Name, Phone FROM Account WHERE Name LIKE '%software%'

### Find records created by specific user
SELECT Id, Name, CreatedDate FROM Account WHERE CreatedById = '005XXXXXXXXXXXX' AND CreatedDate = THIS_MONTH

## Tips and Best Practices

1. Always specify needed fields - SELECT * is not supported
2. Use LIMIT to avoid returning too many records (max 50,000 per query)
3. Use date literals instead of hardcoded dates for maintainability
4. Use relationship queries to reduce API calls
5. Index fields in WHERE clauses for better performance (Id, Name, OwnerId, CreatedDate, custom fields with External ID)
6. Use COUNT() for existence checks instead of retrieving records
7. Escape single quotes in strings: WHERE Name = 'O\\'Reilly'
8. Use bind variables in Apex for dynamic values (prevents SOQL injection)
";
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
            // Query
            case "query":
                var soql = args["soql"]?.ToString();
                return await CallSalesforceApi("GET", $"/query?q={Uri.EscapeDataString(soql)}");

            case "search":
                var sosl = args["sosl"]?.ToString();
                return await CallSalesforceApi("GET", $"/search?q={Uri.EscapeDataString(sosl)}");

            // Records
            case "get_record":
                var fields = args["fields"]?.ToString();
                var getPath = $"/sobjects/{args["object"]}/{args["id"]}";
                if (!string.IsNullOrEmpty(fields)) getPath += $"?fields={fields}";
                return await CallSalesforceApi("GET", getPath);

            case "create_record":
                return await CallSalesforceApi("POST", $"/sobjects/{args["object"]}", args["data"] as JObject);

            case "update_record":
                await CallSalesforceApi("PATCH", $"/sobjects/{args["object"]}/{args["id"]}", args["data"] as JObject);
                return new JObject { ["success"] = true, ["id"] = args["id"] };

            case "delete_record":
                await CallSalesforceApi("DELETE", $"/sobjects/{args["object"]}/{args["id"]}");
                return new JObject { ["success"] = true, ["deleted"] = args["id"] };

            // Metadata
            case "list_objects":
                return await CallSalesforceApi("GET", "/sobjects");

            case "describe_object":
                return await CallSalesforceApi("GET", $"/sobjects/{args["object"]}/describe");

            case "get_limits":
                return await CallSalesforceApi("GET", "/limits");

            // Analytics
            case "list_reports":
                return await CallSalesforceApi("GET", "/analytics/reports");

            case "run_report":
                return await CallSalesforceApi("POST", $"/analytics/reports/{args["report_id"]}");

            case "list_dashboards":
                return await CallSalesforceApi("GET", "/analytics/dashboards");

            // Chatter
            case "post_to_chatter":
                var chatterBody = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["messageSegments"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "Text",
                                ["text"] = args["message"]?.ToString()
                            }
                        }
                    },
                    ["feedElementType"] = "FeedItem",
                    ["subjectId"] = args["subject_id"]?.ToString()
                };
                return await CallSalesforceApi("POST", "/chatter/feed-elements", chatterBody);

            case "get_chatter_feed":
                return await CallSalesforceApi("GET", $"/chatter/feeds/{args["feed_type"]}/feed-elements");

            // Composite
            case "composite":
                var requests = args["requests"] as JArray;
                var compositeBody = new JObject
                {
                    ["allOrNone"] = args["all_or_none"]?.ToObject<bool>() ?? false,
                    ["compositeRequest"] = requests
                };
                return await CallSalesforceApi("POST", "/composite", compositeBody);

            // Knowledge
            case "list_knowledge_articles":
                var kaPath = "/support/knowledgeArticles";
                var kaParams = new List<string>();
                if (!string.IsNullOrEmpty(args["q"]?.ToString())) kaParams.Add($"q={Uri.EscapeDataString(args["q"].ToString())}");
                if (!string.IsNullOrEmpty(args["channel"]?.ToString())) kaParams.Add($"channel={args["channel"]}");
                if (args["page_size"] != null) kaParams.Add($"pageSize={args["page_size"]}");
                if (kaParams.Count > 0) kaPath += "?" + string.Join("&", kaParams);
                return await CallSalesforceApi("GET", kaPath, headers: new Dictionary<string, string> { { "Accept-Language", DEFAULT_LANGUAGE } });

            case "get_knowledge_article":
                var gaPath = $"/support/knowledgeArticles/{args["article_id"]}";
                if (!string.IsNullOrEmpty(args["channel"]?.ToString())) gaPath += $"?channel={args["channel"]}";
                return await CallSalesforceApi("GET", gaPath, headers: new Dictionary<string, string> { { "Accept-Language", DEFAULT_LANGUAGE } });

            case "create_knowledge_article":
                var createArticleBody = new JObject
                {
                    ["articleTypeApiName"] = args["article_type"]?.ToString(),
                    ["title"] = args["title"]?.ToString(),
                    ["urlName"] = args["url_name"]?.ToString()
                };
                if (args["fields"] is JObject articleFields && articleFields.HasValues)
                    createArticleBody["fields"] = articleFields;
                return await CallSalesforceApi("POST", "/knowledgeManagement/articles", createArticleBody);

            case "update_knowledge_article":
                var updateArticleBody = new JObject();
                if (!string.IsNullOrEmpty(args["title"]?.ToString())) updateArticleBody["title"] = args["title"];
                if (args["fields"] is JObject updateFields && updateFields.HasValues)
                    updateArticleBody["fields"] = updateFields;
                await CallSalesforceApi("PATCH", $"/knowledgeManagement/articles/{args["article_id"]}", updateArticleBody);
                return new JObject { ["success"] = true, ["id"] = args["article_id"] };

            case "delete_knowledge_article":
                await CallSalesforceApi("DELETE", $"/knowledgeManagement/articles/{args["article_id"]}");
                return new JObject { ["success"] = true, ["deleted"] = args["article_id"] };

            // Search Suggestions
            case "search_suggestions":
                var ssParams = $"q={Uri.EscapeDataString(args["q"].ToString())}&language={args["language"]}";
                if (!string.IsNullOrEmpty(args["channel"]?.ToString())) ssParams += $"&channel={args["channel"]}";
                return await CallSalesforceApi("GET", $"/search/suggestSearchQueries?{ssParams}", headers: new Dictionary<string, string> { { "Accept-Language", args["language"]?.ToString() ?? DEFAULT_LANGUAGE } });

            case "suggest_article_titles":
                var stParams = $"q={Uri.EscapeDataString(args["q"].ToString())}&language={args["language"]}&publishStatus={args["publish_status"]}";
                if (!string.IsNullOrEmpty(args["article_type"]?.ToString())) stParams += $"&articleType={args["article_type"]}";
                return await CallSalesforceApi("GET", $"/search/suggestTitleMatches?{stParams}", headers: new Dictionary<string, string> { { "Accept-Language", args["language"]?.ToString() ?? DEFAULT_LANGUAGE } });

            // Synonyms (Tooling API)
            case "list_synonym_groups":
                return await CallSalesforceApi("GET", "/tooling/query?q=" + Uri.EscapeDataString("SELECT Id, GroupName, Synonyms FROM SearchSynonymGroup ORDER BY GroupName"));

            case "get_synonym_group":
                return await CallSalesforceApi("GET", $"/tooling/sobjects/SearchSynonymGroup/{args["id"]}");

            case "create_synonym_group":
                var createSynBody = new JObject
                {
                    ["GroupName"] = args["group_name"]?.ToString(),
                    ["Synonyms"] = args["synonyms"]?.ToString()
                };
                return await CallSalesforceApi("POST", "/tooling/sobjects/SearchSynonymGroup", createSynBody);

            case "update_synonym_group":
                var updateSynBody = new JObject();
                if (!string.IsNullOrEmpty(args["group_name"]?.ToString())) updateSynBody["GroupName"] = args["group_name"];
                if (!string.IsNullOrEmpty(args["synonyms"]?.ToString())) updateSynBody["Synonyms"] = args["synonyms"];
                await CallSalesforceApi("PATCH", $"/tooling/sobjects/SearchSynonymGroup/{args["id"]}", updateSynBody);
                return new JObject { ["success"] = true, ["id"] = args["id"] };

            case "delete_synonym_group":
                await CallSalesforceApi("DELETE", $"/tooling/sobjects/SearchSynonymGroup/{args["id"]}");
                return new JObject { ["success"] = true, ["deleted"] = args["id"] };

            // Deflection Tracking
            case "log_interaction":
            {
                var interactionBody = new JObject
                {
                    ["Outcome__c"] = args["outcome"]?.ToString(),
                    ["Summary__c"] = args["summary"]?.ToString()
                };

                if (!string.IsNullOrEmpty(args["severity"]?.ToString()))
                    interactionBody["Severity__c"] = args["severity"];
                if (!string.IsNullOrEmpty(args["topic"]?.ToString()))
                    interactionBody["Topic__c"] = args["topic"];
                if (!string.IsNullOrEmpty(args["channel"]?.ToString()))
                    interactionBody["Channel__c"] = args["channel"];
                if (!string.IsNullOrEmpty(args["conversation_id"]?.ToString()))
                    interactionBody["Conversation_Id__c"] = args["conversation_id"];
                if (!string.IsNullOrEmpty(args["user_email"]?.ToString()))
                    interactionBody["User_Email__c"] = args["user_email"];
                if (!string.IsNullOrEmpty(args["case_id"]?.ToString()))
                    interactionBody["Case__c"] = args["case_id"];

                var kbCount = args["kb_articles_shown"]?.ToObject<int?>() ?? 0;
                interactionBody["KB_Articles_Shown__c"] = kbCount;

                var logResult = await CallSalesforceApi("POST", "/sobjects/Copilot_Interaction__c", interactionBody);
                var interactionId = logResult["id"]?.ToString();

                if (!string.IsNullOrEmpty(interactionId))
                {
                    var detail = await CallSalesforceApi("GET",
                        $"/sobjects/Copilot_Interaction__c/{interactionId}?fields=Name,Outcome__c,Severity__c,Topic__c,Channel__c");
                    return detail;
                }

                return logResult;
            }

            // Phase 3 — Autonomous Actions

            case "get_case_timeline":
            {
                var timelineCaseId = args["case_id"]?.ToString();

                // Fetch case details, comments, emails, and feed items in parallel via composite
                var timelineComposite = new JObject
                {
                    ["allOrNone"] = false,
                    ["compositeRequest"] = new JArray
                    {
                        new JObject
                        {
                            ["method"] = "GET",
                            ["url"] = $"/services/data/v66.0/sobjects/Case/{timelineCaseId}?fields=CaseNumber,Subject,Description,Status,Priority,Type,Reason,Origin,CreatedDate,ClosedDate,ContactEmail,Contact.Name,Account.Name",
                            ["referenceId"] = "caseDetail"
                        },
                        new JObject
                        {
                            ["method"] = "GET",
                            ["url"] = $"/services/data/v66.0/query?q={Uri.EscapeDataString($"SELECT Id, CommentBody, CreatedBy.Name, CreatedDate, IsPublished FROM CaseComment WHERE ParentId = '{timelineCaseId}' ORDER BY CreatedDate ASC")}",
                            ["referenceId"] = "comments"
                        },
                        new JObject
                        {
                            ["method"] = "GET",
                            ["url"] = $"/services/data/v66.0/query?q={Uri.EscapeDataString($"SELECT Id, Subject, TextBody, FromAddress, ToAddress, Status, MessageDate, Incoming FROM EmailMessage WHERE ParentId = '{timelineCaseId}' ORDER BY MessageDate ASC")}",
                            ["referenceId"] = "emails"
                        },
                        new JObject
                        {
                            ["method"] = "GET",
                            ["url"] = $"/services/data/v66.0/query?q={Uri.EscapeDataString($"SELECT Id, Type, Body, CreatedBy.Name, CreatedDate FROM FeedItem WHERE ParentId = '{timelineCaseId}' ORDER BY CreatedDate ASC LIMIT 50")}",
                            ["referenceId"] = "feedItems"
                        }
                    }
                };

                var timelineResult = await CallSalesforceApi("POST", "/composite", timelineComposite);

                // Flatten into a clean timeline object
                var compositeResults = timelineResult["compositeResponse"] as JArray ?? new JArray();
                var timeline = new JObject();
                foreach (var cr in compositeResults)
                {
                    var refId = cr["referenceId"]?.ToString();
                    var statusCode = cr["httpStatusCode"]?.ToObject<int>() ?? 0;
                    if (statusCode >= 200 && statusCode < 300)
                        timeline[refId] = cr["body"];
                    else
                        timeline[refId] = new JObject { ["error"] = $"HTTP {statusCode}" };
                }
                return timeline;
            }

            case "send_case_email":
            {
                var emailBody = new JObject
                {
                    ["ParentId"] = args["case_id"]?.ToString(),
                    ["ToAddress"] = args["to_address"]?.ToString(),
                    ["Subject"] = args["subject"]?.ToString(),
                    ["HtmlBody"] = args["body"]?.ToString(),
                    ["Status"] = args["status"]?.ToString() ?? "Draft"
                };

                var emailResult = await CallSalesforceApi("POST", "/sobjects/EmailMessage", emailBody);
                var emailId = emailResult["id"]?.ToString();

                if (!string.IsNullOrEmpty(emailId))
                {
                    var emailDetail = await CallSalesforceApi("GET",
                        $"/sobjects/EmailMessage/{emailId}?fields=Id,Subject,ToAddress,Status,MessageDate,ParentId");
                    return emailDetail;
                }

                return emailResult;
            }

            case "suggest_kb_for_case":
            {
                // Get case subject and description for search context
                var suggestCaseId = args["case_id"]?.ToString();
                var caseInfo = await CallSalesforceApi("GET",
                    $"/sobjects/Case/{suggestCaseId}?fields=Subject,Description");

                var caseSubject = caseInfo["Subject"]?.ToString() ?? "";
                var caseDesc = caseInfo["Description"]?.ToString() ?? "";
                var extraTerms = args["search_terms"]?.ToString() ?? "";

                // Build search query from case context
                var searchQuery = caseSubject;
                if (!string.IsNullOrEmpty(extraTerms))
                    searchQuery = $"{extraTerms} {searchQuery}";

                // Search Knowledge articles
                var kbPath = $"/support/knowledgeArticles?q={Uri.EscapeDataString(searchQuery)}&pageSize=10";
                var kbResults = await CallSalesforceApi("GET", kbPath,
                    headers: new Dictionary<string, string> { { "Accept-Language", DEFAULT_LANGUAGE } });

                // Return results with the case context for reference
                return new JObject
                {
                    ["case"] = new JObject { ["Id"] = suggestCaseId, ["Subject"] = caseSubject },
                    ["searchQuery"] = searchQuery,
                    ["articles"] = kbResults["articles"] ?? new JArray()
                };
            }

            case "draft_kb_article":
            {
                var draftCaseId = args["case_id"]?.ToString();
                var draftArticleType = args["article_type"]?.ToString() ?? "Knowledge__kav";

                var draftBody = new JObject
                {
                    ["articleTypeApiName"] = draftArticleType,
                    ["title"] = args["title"]?.ToString(),
                    ["urlName"] = args["url_name"]?.ToString()
                };

                var draftFields = new JObject();
                if (!string.IsNullOrEmpty(args["summary"]?.ToString()))
                    draftFields["Summary"] = args["summary"];
                if (!string.IsNullOrEmpty(args["content"]?.ToString()))
                    draftFields["Content__c"] = args["content"];
                // Link back to the originating case
                draftFields["SourceId"] = draftCaseId;

                if (draftFields.HasValues)
                    draftBody["fields"] = draftFields;

                var articleResult = await CallSalesforceApi("POST", "/knowledgeManagement/articles", draftBody);
                return new JObject
                {
                    ["success"] = true,
                    ["articleId"] = articleResult["id"],
                    ["caseId"] = draftCaseId,
                    ["title"] = args["title"]
                };
            }

            case "categorize_case":
            {
                var catCaseId = args["case_id"]?.ToString();
                var catBody = new JObject
                {
                    ["Type"] = args["type"]?.ToString(),
                    ["Reason"] = args["reason"]?.ToString()
                };

                if (!string.IsNullOrEmpty(args["subject"]?.ToString()))
                    catBody["Subject"] = args["subject"];

                await CallSalesforceApi("PATCH", $"/sobjects/Case/{catCaseId}", catBody);

                // Add internal comment with categorization rationale
                var catComment = args["internal_comments"]?.ToString();
                if (!string.IsNullOrEmpty(catComment))
                {
                    var commentBody = new JObject
                    {
                        ["ParentId"] = catCaseId,
                        ["CommentBody"] = catComment,
                        ["IsPublished"] = false
                    };
                    await CallSalesforceApi("POST", "/sobjects/CaseComment", commentBody);
                }

                // Return updated case
                var updatedCase = await CallSalesforceApi("GET",
                    $"/sobjects/Case/{catCaseId}?fields=CaseNumber,Subject,Type,Reason,Status,Priority");
                return updatedCase;
            }

            // Case Creation
            case "create_case":
            {
                var caseBody = new JObject
                {
                    ["Subject"] = args["subject"]?.ToString(),
                    ["Description"] = args["description"]?.ToString(),
                    ["Priority"] = args["priority"]?.ToString() ?? "Medium",
                    ["Origin"] = args["origin"]?.ToString() ?? "Chat"
                };

                if (!string.IsNullOrEmpty(args["type"]?.ToString()))
                    caseBody["Type"] = args["type"];

                // Look up ContactId by email if provided
                var contactEmail = args["contact_email"]?.ToString();
                if (!string.IsNullOrEmpty(contactEmail))
                {
                    var contactQuery = await CallSalesforceApi("GET",
                        $"/query?q={Uri.EscapeDataString($"SELECT Id FROM Contact WHERE Email = '{contactEmail.Replace("'", "\\'")}' LIMIT 1")}");
                    var contactRecords = contactQuery["records"] as JArray;
                    if (contactRecords != null && contactRecords.Count > 0)
                        caseBody["ContactId"] = contactRecords[0]["Id"]?.ToString();
                }

                var createResult = await CallSalesforceApi("POST", "/sobjects/Case", caseBody);
                var caseId = createResult["id"]?.ToString();

                // Query back for CaseNumber
                if (!string.IsNullOrEmpty(caseId))
                {
                    var caseDetail = await CallSalesforceApi("GET",
                        $"/sobjects/Case/{caseId}?fields=CaseNumber,Id,Subject,Priority,Status");
                    return caseDetail;
                }

                return createResult;
            }

            // --- Phase 5: Reporting & Analytics ---

            case "get_case_trends":
            {
                var groupField = args["group_by"]?.ToString() ?? "Type";
                var days = args["days"]?.Value<int>() ?? 30;
                var minCount = args["min_count"]?.Value<int>() ?? 1;

                // Allowed standard fields — custom fields pass through if they end with __c
                var allowedFields = new HashSet<string> { "Type", "Reason", "Priority", "Status", "Origin" };
                if (!allowedFields.Contains(groupField) && !groupField.EndsWith("__c"))
                    throw new ArgumentException($"Invalid group_by field: {groupField}");

                var sinceDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
                var trendSoql = $"SELECT {groupField}, COUNT(Id) cnt FROM Case WHERE CreatedDate >= {sinceDate} GROUP BY {groupField} HAVING COUNT(Id) >= {minCount} ORDER BY COUNT(Id) DESC";
                var result = await CallSalesforceApi("GET", $"/query?q={Uri.EscapeDataString(trendSoql)}");
                return result;
            }

            case "get_deflection_metrics":
            {
                var days = args["days"]?.Value<int>() ?? 30;
                var groupBy = args["group_by"]?.ToString() ?? "none";
                var sinceDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

                string deflectionSoql;
                if (groupBy == "none" || string.IsNullOrEmpty(groupBy))
                {
                    deflectionSoql = $"SELECT Outcome__c, COUNT(Id) cnt FROM Copilot_Interaction__c WHERE CreatedDate >= {sinceDate} GROUP BY Outcome__c ORDER BY COUNT(Id) DESC";
                }
                else
                {
                    var allowedGroupFields = new HashSet<string> { "Topic__c", "Channel__c", "Severity__c" };
                    if (!allowedGroupFields.Contains(groupBy))
                        throw new ArgumentException($"Invalid group_by field: {groupBy}. Allowed: Topic__c, Channel__c, Severity__c");

                    deflectionSoql = $"SELECT Outcome__c, {groupBy}, COUNT(Id) cnt FROM Copilot_Interaction__c WHERE CreatedDate >= {sinceDate} GROUP BY Outcome__c, {groupBy} ORDER BY COUNT(Id) DESC";
                }

                var result = await CallSalesforceApi("GET", $"/query?q={Uri.EscapeDataString(deflectionSoql)}");
                return result;
            }

            case "get_sla_compliance":
            {
                var days = args["days"]?.Value<int>() ?? 30;
                var priorityFilter = args["priority"]?.ToString();
                var sinceDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

                // Query cases with their priority and first response timestamps
                var whereClause = $"CreatedDate >= {sinceDate}";
                if (!string.IsNullOrEmpty(priorityFilter))
                    whereClause += $" AND Priority = '{priorityFilter.Replace("'", "\\'")}'";

                var slaSoql = $"SELECT Id, CaseNumber, Subject, Priority, Status, CreatedDate, " +
                           $"(SELECT CreatedDate FROM CaseComments ORDER BY CreatedDate ASC LIMIT 1), " +
                           $"(SELECT CreatedDate FROM EmailMessages WHERE Incoming = false ORDER BY CreatedDate ASC LIMIT 1) " +
                           $"FROM Case WHERE {whereClause} ORDER BY CreatedDate DESC LIMIT 200";

                var result = await CallSalesforceApi("GET", $"/query?q={Uri.EscapeDataString(slaSoql)}");

                // SLA thresholds in hours
                var slaThresholds = new Dictionary<string, double>
                {
                    ["Critical"] = 1,
                    ["High"] = 4,
                    ["Medium"] = 24,
                    ["Low"] = 48
                };

                var cases = result["records"] as JArray ?? new JArray();
                var summary = new JObject();
                var breachedCases = new JArray();

                foreach (var c in cases)
                {
                    var priority = c["Priority"]?.ToString() ?? "Medium";
                    var created = c["CreatedDate"]?.Value<DateTime>() ?? DateTime.UtcNow;

                    // Find earliest response (comment or outbound email)
                    DateTime? firstResponse = null;
                    var comments = c["CaseComments"]?["records"] as JArray;
                    var emails = c["EmailMessages"]?["records"] as JArray;

                    if (comments != null && comments.Count > 0)
                        firstResponse = comments[0]["CreatedDate"]?.Value<DateTime>();
                    if (emails != null && emails.Count > 0)
                    {
                        var emailDate = emails[0]["CreatedDate"]?.Value<DateTime>();
                        if (emailDate.HasValue && (!firstResponse.HasValue || emailDate.Value < firstResponse.Value))
                            firstResponse = emailDate;
                    }

                    if (!summary.ContainsKey(priority))
                        summary[priority] = new JObject { ["total"] = 0, ["within_sla"] = 0, ["breached"] = 0, ["no_response"] = 0 };

                    var pStats = summary[priority] as JObject;
                    pStats["total"] = pStats["total"].Value<int>() + 1;

                    if (firstResponse.HasValue)
                    {
                        var responseHours = (firstResponse.Value - created).TotalHours;
                        var threshold = slaThresholds.ContainsKey(priority) ? slaThresholds[priority] : 24;

                        if (responseHours <= threshold)
                        {
                            pStats["within_sla"] = pStats["within_sla"].Value<int>() + 1;
                        }
                        else
                        {
                            pStats["breached"] = pStats["breached"].Value<int>() + 1;
                            breachedCases.Add(new JObject
                            {
                                ["CaseNumber"] = c["CaseNumber"],
                                ["Id"] = c["Id"],
                                ["Subject"] = c["Subject"],
                                ["Priority"] = priority,
                                ["ResponseHours"] = Math.Round(responseHours, 1),
                                ["SlaThresholdHours"] = slaThresholds.ContainsKey(priority) ? slaThresholds[priority] : 24
                            });
                        }
                    }
                    else
                    {
                        pStats["no_response"] = pStats["no_response"].Value<int>() + 1;
                    }
                }

                return new JObject
                {
                    ["sla_thresholds"] = JObject.FromObject(slaThresholds),
                    ["compliance_by_priority"] = summary,
                    ["breached_cases"] = breachedCases,
                    ["period_days"] = days,
                    ["total_cases_analyzed"] = cases.Count
                };
            }

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private async Task<JObject> CallSalesforceApi(string method, string path, JObject body = null, Dictionary<string, string> headers = null)
    {
        var instanceUrl = GetInstanceUrl();
        var accessToken = await GetAccessTokenAsync("api");
        var fullPath = $"https://{instanceUrl}/services/data/{API_VERSION}{path}";

        var request = new HttpRequestMessage(new HttpMethod(method), fullPath);

        // Set Bearer token from client_credentials flow
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Add custom headers
        if (headers != null)
        {
            foreach (var h in headers)
                request.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await Context.SendAsync(request, CancellationToken);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            await LogToAppInsights("SalesforceAPIError", new
            {
                Method = method,
                Path = path,
                StatusCode = (int)response.StatusCode,
                ErrorBody = TruncateForLog(content, 500)
            });
            throw new HttpRequestException($"Salesforce API returned {(int)response.StatusCode}: {content}");
        }

        if (string.IsNullOrEmpty(content))
            return new JObject();

        // Handle array responses
        if (content.TrimStart().StartsWith("["))
        {
            var arr = JArray.Parse(content);
            return new JObject { ["items"] = arr };
        }

        return JObject.Parse(content);
    }

    #endregion

    #region Client Credentials Auth

    private string GetInstanceUrl()
    {
        // Prefer the instance_url from the token response (Salesforce may redirect)
        if (!string.IsNullOrEmpty(_cachedInstanceUrl))
            return _cachedInstanceUrl;

        // Read from X-SF-Instance-URL header (injected by policyTemplate from connectionParameters)
        IEnumerable<string> values;
        string instanceUrl = null;
        if (this.Context.Request.Headers.TryGetValues("X-SF-Instance-URL", out values))
            instanceUrl = values.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(instanceUrl))
            throw new InvalidOperationException("Instance URL is required. Check your connection's Instance URL parameter.");
        // Strip protocol if user included it
        instanceUrl = instanceUrl.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        return instanceUrl;
    }

    private async Task<string> GetAccessTokenAsync(string correlationId)
    {
        // Return cached token if already acquired this request
        if (!string.IsNullOrEmpty(_cachedAccessToken))
            return _cachedAccessToken;

        var instanceUrl = GetInstanceUrl();

        var tokenUrl = $"https://{instanceUrl}/services/oauth2/token";

        // Salesforce natively accepts Basic auth header for client_credentials flow
        // Format: Authorization: Basic base64(client_id:client_secret)
        // Power Platform sends this automatically from username/password connection params
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        tokenRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

        // Forward the Basic auth header from Power Platform directly to Salesforce
        if (this.Context.Request.Headers.Authorization != null)
            tokenRequest.Headers.Authorization = this.Context.Request.Headers.Authorization;

        var tokenResponse = await this.Context.SendAsync(tokenRequest, this.CancellationToken).ConfigureAwait(false);
        var tokenContent = await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!tokenResponse.IsSuccessStatusCode)
        {
            await LogToAppInsights("TokenAcquisitionFailed", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)tokenResponse.StatusCode,
                Error = TruncateForLog(tokenContent, 500)
            });
            throw new HttpRequestException($"Failed to acquire Salesforce access token: {(int)tokenResponse.StatusCode} - {tokenContent}");
        }

        var tokenObj = JObject.Parse(tokenContent);
        _cachedAccessToken = tokenObj["access_token"]?.ToString();

        // Use instance_url from token response for subsequent API calls
        var responseInstanceUrl = tokenObj["instance_url"]?.ToString();
        if (!string.IsNullOrEmpty(responseInstanceUrl))
            _cachedInstanceUrl = responseInstanceUrl.Replace("https://", "").Replace("http://", "").TrimEnd('/');

        if (string.IsNullOrEmpty(_cachedAccessToken))
            throw new InvalidOperationException("Salesforce token response did not contain an access_token.");

        await LogToAppInsights("TokenAcquired", new
        {
            CorrelationId = correlationId,
            InstanceUrl = _cachedInstanceUrl ?? instanceUrl,
            TokenLength = _cachedAccessToken.Length
        });

        return _cachedAccessToken;
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
            var telemetryResponse = await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
            if (!telemetryResponse.IsSuccessStatusCode)
            {
                var errorBody = await telemetryResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                _lastTelemetryError = $"AppInsights returned {(int)telemetryResponse.StatusCode}: {errorBody}";
            }
        }
        catch (Exception ex)
        {
            _lastTelemetryError = $"Telemetry send failed: {ex.GetType().Name}: {ex.Message}";
        }
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

    private string TruncateForLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }

    #endregion
}
