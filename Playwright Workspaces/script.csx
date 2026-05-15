using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
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
                ["name"] = "playwright-workspaces-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // Session Management
            CreateTool("create_session", "Create a remote cloud browser session on Azure Playwright Workspaces. Optionally navigate to a start URL. Returns a session ID for subsequent operations.",
                new JObject
                {
                    ["url"] = new JObject { ["type"] = "string", ["description"] = "Initial URL to navigate to (optional)" },
                    ["ttl_minutes"] = new JObject { ["type"] = "integer", ["description"] = "Session lifetime in minutes (default: 15, max: 60)" },
                    ["os"] = new JObject { ["type"] = "string", ["description"] = "Remote browser OS: linux or windows (default: linux)" }
                },
                Array.Empty<string>()),

            CreateTool("get_session", "Get the current status of a browser session.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" }
                },
                new[] { "session_id" }),

            CreateTool("close_session", "Close a browser session and release the remote browser.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" }
                },
                new[] { "session_id" }),

            // Navigation
            CreateTool("navigate", "Navigate to a URL in the remote browser, preserving cookies and state.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["url"] = new JObject { ["type"] = "string", ["description"] = "URL to navigate to" },
                    ["wait_until"] = new JObject { ["type"] = "string", ["description"] = "When to consider navigation complete: load, domcontentloaded, networkidle (default: networkidle)" },
                    ["wait_for_selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector to wait for after navigation (optional)" }
                },
                new[] { "session_id", "url" }),

            // Interaction
            CreateTool("click", "Click an element by CSS selector, text content, or ARIA role.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of element to click" },
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "Click element containing this text (alternative to selector)" },
                    ["role"] = new JObject { ["type"] = "string", ["description"] = "ARIA role of element (e.g., button, link)" },
                    ["role_name"] = new JObject { ["type"] = "string", ["description"] = "Accessible name when using role-based click" }
                },
                new[] { "session_id" }),

            CreateTool("type_text", "Type text into an input field by selector, label, or placeholder.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "Text to type" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of input" },
                    ["label"] = new JObject { ["type"] = "string", ["description"] = "Label text of input (alternative to selector)" },
                    ["placeholder"] = new JObject { ["type"] = "string", ["description"] = "Placeholder text (alternative to selector)" },
                    ["submit"] = new JObject { ["type"] = "boolean", ["description"] = "Press Enter after typing" },
                    ["clear"] = new JObject { ["type"] = "boolean", ["description"] = "Clear existing text first (default: true)" }
                },
                new[] { "session_id", "text" }),

            CreateTool("select_option", "Select an option from a dropdown.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of select element" },
                    ["value"] = new JObject { ["type"] = "string", ["description"] = "Option value to select" },
                    ["label"] = new JObject { ["type"] = "string", ["description"] = "Option label (alternative to value)" }
                },
                new[] { "session_id", "selector" }),

            CreateTool("fill_form", "Fill multiple form fields at once with a field-to-value mapping.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["fields"] = new JObject { ["type"] = "object", ["description"] = "Map of selectors/labels to values" },
                    ["submit"] = new JObject { ["type"] = "boolean", ["description"] = "Click submit after filling" },
                    ["submit_selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of submit button" }
                },
                new[] { "session_id", "fields" }),

            // Extraction
            CreateTool("get_page_content", "Get the text, HTML, or markdown content of the current page.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["format"] = new JObject { ["type"] = "string", ["description"] = "Output format: text, html, or markdown (default: text)" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector for a specific element (optional)" }
                },
                new[] { "session_id" }),

            CreateTool("get_element_text", "Get text content or attribute from elements matching a selector.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector" },
                    ["all"] = new JObject { ["type"] = "boolean", ["description"] = "Return all matches (default: first only)" },
                    ["attribute"] = new JObject { ["type"] = "string", ["description"] = "Get attribute value instead (e.g., href, src)" }
                },
                new[] { "session_id", "selector" }),

            CreateTool("scrape_data", "Extract structured data using CSS selectors. Returns JSON array of items.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["container_selector"] = new JObject { ["type"] = "string", ["description"] = "Repeating item container selector (e.g., .product-card)" },
                    ["fields"] = new JObject { ["type"] = "object", ["description"] = "Map of field names to selectors. Append @attr for attributes (e.g., link: a@href)" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Max items to extract (default: 50)" }
                },
                new[] { "session_id", "fields" }),

            // Utility
            CreateTool("take_screenshot", "Capture a screenshot of the current page or a specific element.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["full_page"] = new JObject { ["type"] = "boolean", ["description"] = "Capture full scrollable page (default: false)" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "Capture a specific element only (optional)" }
                },
                new[] { "session_id" }),

            CreateTool("evaluate_js", "Execute JavaScript on the page and return the result.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["script"] = new JObject { ["type"] = "string", ["description"] = "JavaScript code to execute" }
                },
                new[] { "session_id", "script" }),

            CreateTool("wait_for", "Wait for an element to appear, become visible, or disappear.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector to wait for" },
                    ["state"] = new JObject { ["type"] = "string", ["description"] = "State: visible, hidden, attached, detached (default: visible)" },
                    ["timeout_ms"] = new JObject { ["type"] = "integer", ["description"] = "Timeout in ms (default: 30000)" }
                },
                new[] { "session_id", "selector" }),

            CreateTool("scroll", "Scroll the page or a specific element.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["direction"] = new JObject { ["type"] = "string", ["description"] = "Direction: up, down, top, bottom (default: down)" },
                    ["amount"] = new JObject { ["type"] = "integer", ["description"] = "Pixels to scroll (default: 500)" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "Element to scroll (optional)" }
                },
                new[] { "session_id" })
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
            // Session Management
            case "create_session":
                var createBody = new JObject();
                if (args["url"] != null) createBody["url"] = args["url"];
                if (args["ttl_minutes"] != null) createBody["ttlMinutes"] = args["ttl_minutes"];
                if (args["os"] != null) createBody["os"] = args["os"];
                return await CallBrokerApi("POST", "/sessions", createBody);

            case "get_session":
                return await CallBrokerApi("GET", $"/sessions/{args["session_id"]}");

            case "close_session":
                await CallBrokerApi("DELETE", $"/sessions/{args["session_id"]}");
                return new JObject { ["success"] = true, ["closed"] = args["session_id"] };

            // Navigation
            case "navigate":
                var navBody = new JObject { ["url"] = args["url"] };
                if (args["wait_until"] != null) navBody["waitUntil"] = args["wait_until"];
                if (args["wait_for_selector"] != null) navBody["waitForSelector"] = args["wait_for_selector"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/navigate", navBody);

            // Interaction
            case "click":
                var clickBody = new JObject();
                if (args["selector"] != null) clickBody["selector"] = args["selector"];
                if (args["text"] != null) clickBody["text"] = args["text"];
                if (args["role"] != null) clickBody["role"] = args["role"];
                if (args["role_name"] != null) clickBody["roleName"] = args["role_name"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/click", clickBody);

            case "type_text":
                var typeBody = new JObject { ["text"] = args["text"] };
                if (args["selector"] != null) typeBody["selector"] = args["selector"];
                if (args["label"] != null) typeBody["label"] = args["label"];
                if (args["placeholder"] != null) typeBody["placeholder"] = args["placeholder"];
                if (args["submit"] != null) typeBody["submit"] = args["submit"];
                if (args["clear"] != null) typeBody["clear"] = args["clear"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/type", typeBody);

            case "select_option":
                var selectBody = new JObject { ["selector"] = args["selector"] };
                if (args["value"] != null) selectBody["value"] = args["value"];
                if (args["label"] != null) selectBody["label"] = args["label"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/select", selectBody);

            case "fill_form":
                var fillBody = new JObject { ["fields"] = args["fields"] };
                if (args["submit"] != null) fillBody["submit"] = args["submit"];
                if (args["submit_selector"] != null) fillBody["submitSelector"] = args["submit_selector"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/fill-form", fillBody);

            // Extraction
            case "get_page_content":
                var contentFormat = args["format"]?.ToString() ?? "text";
                var contentSelector = args["selector"]?.ToString();
                var contentQuery = $"?format={contentFormat}";
                if (contentSelector != null) contentQuery += $"&selector={Uri.EscapeDataString(contentSelector)}";
                return await CallBrokerApi("GET", $"/sessions/{args["session_id"]}/content{contentQuery}");

            case "get_element_text":
                var elemBody = new JObject { ["selector"] = args["selector"] };
                if (args["all"] != null) elemBody["all"] = args["all"];
                if (args["attribute"] != null) elemBody["attribute"] = args["attribute"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/element-text", elemBody);

            case "scrape_data":
                var scrapeBody = new JObject { ["fields"] = args["fields"] };
                if (args["container_selector"] != null) scrapeBody["containerSelector"] = args["container_selector"];
                if (args["limit"] != null) scrapeBody["limit"] = args["limit"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/scrape", scrapeBody);

            // Utility
            case "take_screenshot":
                var fullPage = args["full_page"]?.ToObject<bool>() ?? false;
                var ssSelector = args["selector"]?.ToString();
                var ssQuery = $"?fullPage={fullPage}";
                if (ssSelector != null) ssQuery += $"&selector={Uri.EscapeDataString(ssSelector)}";
                return await CallBrokerApi("GET", $"/sessions/{args["session_id"]}/screenshot{ssQuery}");

            case "evaluate_js":
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/evaluate", new JObject
                {
                    ["script"] = args["script"]
                });

            case "wait_for":
                var waitBody = new JObject { ["selector"] = args["selector"] };
                if (args["state"] != null) waitBody["state"] = args["state"];
                if (args["timeout_ms"] != null) waitBody["timeout"] = args["timeout_ms"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/wait", waitBody);

            case "scroll":
                var scrollBody = new JObject();
                if (args["direction"] != null) scrollBody["direction"] = args["direction"];
                if (args["amount"] != null) scrollBody["amount"] = args["amount"];
                if (args["selector"] != null) scrollBody["selector"] = args["selector"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/scroll", scrollBody);

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private async Task<JObject> CallBrokerApi(string method, string path, JObject body = null)
    {
        var brokerUrl = Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var fullPath = $"{brokerUrl}/api{path}";

        var request = new HttpRequestMessage(new HttpMethod(method), fullPath);

        // Forward API key header
        if (Context.Request.Headers.Contains("X-API-Key"))
        {
            var apiKey = Context.Request.Headers.GetValues("X-API-Key").FirstOrDefault();
            request.Headers.Add("X-API-Key", apiKey);
        }

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && method != "GET" && method != "DELETE")
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await Context.SendAsync(request, CancellationToken);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Broker API returned {(int)response.StatusCode}: {content}");

        if (string.IsNullOrEmpty(content))
            return new JObject { ["success"] = true };

        return JObject.Parse(content);
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
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ingestionEndpoint}/v2/track");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            await Context.SendAsync(request, CancellationToken);
        }
        catch { /* Silent fail for telemetry */ }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0].Trim().Equals("InstrumentationKey", StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
        {
            var kv = part.Split(new[] { '=' }, 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("IngestionEndpoint", StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim().TrimEnd('/');
        }
        return "https://dc.applicationinsights.azure.com";
    }

    #endregion
}
