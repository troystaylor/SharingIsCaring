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
                ["name"] = "webmcp-discovery-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // Discovery Tools
            CreateTool("discover_tools", "Navigate to a web page and discover all WebMCP tools or Playwright fallback tools. Use this first to see what's available on a page.",
                new JObject
                {
                    ["url"] = new JObject { ["type"] = "string", ["description"] = "URL of the web page to scan" },
                    ["wait_for_selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector to wait for before reading tools (optional)" }
                },
                new[] { "url" }),

            // Session Tools
            CreateTool("create_session", "Create a persistent browser session for multi-step interactions. Use when you need to maintain state between operations.",
                new JObject
                {
                    ["url"] = new JObject { ["type"] = "string", ["description"] = "Initial URL to navigate to" },
                    ["ttl_minutes"] = new JObject { ["type"] = "integer", ["description"] = "Session lifetime in minutes (default: 15, max: 60)" }
                },
                new[] { "url" }),

            CreateTool("get_session", "Get the current status of a browser session including available tools.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" }
                },
                new[] { "session_id" }),

            CreateTool("close_session", "Close a browser session to release resources.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" }
                },
                new[] { "session_id" }),

            CreateTool("navigate", "Navigate to a new URL within an existing session, preserving cookies and state.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["url"] = new JObject { ["type"] = "string", ["description"] = "URL to navigate to" }
                },
                new[] { "session_id", "url" }),

            CreateTool("list_session_tools", "List all tools available on the current page in a session.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" }
                },
                new[] { "session_id" }),

            // Execution Tools
            CreateTool("call_tool", "Execute a tool within a browser session. Works with both WebMCP tools and Playwright browser_* tools.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["tool_name"] = new JObject { ["type"] = "string", ["description"] = "Name of the tool to call" },
                    ["input"] = new JObject { ["type"] = "object", ["description"] = "Input parameters for the tool" }
                },
                new[] { "session_id", "tool_name" }),

            CreateTool("execute_stateless", "Execute a single tool in a fresh browser without maintaining session. Good for one-off operations.",
                new JObject
                {
                    ["url"] = new JObject { ["type"] = "string", ["description"] = "URL of the page containing the tool" },
                    ["tool_name"] = new JObject { ["type"] = "string", ["description"] = "Name of the tool to execute" },
                    ["input"] = new JObject { ["type"] = "object", ["description"] = "Input parameters for the tool" }
                },
                new[] { "url", "tool_name" }),

            // Authentication
            CreateTool("inject_auth", "Inject authentication into a session (cookies, localStorage, headers).",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["cookies"] = new JObject { ["type"] = "array", ["description"] = "Cookies to inject", ["items"] = new JObject { ["type"] = "object" } },
                    ["local_storage"] = new JObject { ["type"] = "object", ["description"] = "Key-value pairs for localStorage" },
                    ["headers"] = new JObject { ["type"] = "object", ["description"] = "HTTP headers to set on requests" }
                },
                new[] { "session_id" }),

            // Utility
            CreateTool("take_screenshot", "Capture a screenshot of the current page in a session.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["full_page"] = new JObject { ["type"] = "boolean", ["description"] = "Capture full scrollable page (default: false)" }
                },
                new[] { "session_id" }),

            // Playwright Fallback Tools (always available)
            CreateTool("browser_click", "Click an element on the page (requires active session).",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of element to click" }
                },
                new[] { "session_id", "selector" }),

            CreateTool("browser_type", "Type text into an input field (requires active session).",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of input element" },
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "Text to type" },
                    ["submit"] = new JObject { ["type"] = "boolean", ["description"] = "Press Enter after typing" }
                },
                new[] { "session_id", "selector", "text" }),

            CreateTool("browser_get_text", "Get text content from an element (requires active session).",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of element" }
                },
                new[] { "session_id", "selector" }),

            CreateTool("browser_wait", "Wait for an element to appear, or wait a fixed time.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector to wait for (optional)" },
                    ["timeout_ms"] = new JObject { ["type"] = "integer", ["description"] = "Timeout in milliseconds (default: 30000)" },
                    ["state"] = new JObject { ["type"] = "string", ["description"] = "State to wait for: visible, hidden, attached, detached (default: visible)" }
                },
                new[] { "session_id" }),

            CreateTool("browser_select", "Select an option from a dropdown/select element.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of select element" },
                    ["value"] = new JObject { ["type"] = "string", ["description"] = "Option value to select" },
                    ["label"] = new JObject { ["type"] = "string", ["description"] = "Option label/text to select (alternative to value)" }
                },
                new[] { "session_id", "selector" }),

            CreateTool("browser_get_page", "Get the full page content (HTML or text).",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["format"] = new JObject { ["type"] = "string", ["description"] = "Output format: html or text (default: text)" }
                },
                new[] { "session_id" }),

            CreateTool("browser_evaluate", "Execute JavaScript code on the page and return the result.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["script"] = new JObject { ["type"] = "string", ["description"] = "JavaScript code to execute" }
                },
                new[] { "session_id", "script" }),

            CreateTool("browser_scroll", "Scroll the page or an element.",
                new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID" },
                    ["direction"] = new JObject { ["type"] = "string", ["description"] = "Scroll direction: up, down, left, right, top, bottom (default: down)" },
                    ["amount"] = new JObject { ["type"] = "integer", ["description"] = "Pixels to scroll (default: 500)" },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of element to scroll (optional, scrolls page if omitted)" }
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
            // Discovery
            case "discover_tools":
                return await CallBrokerApi("POST", "/discover", new JObject
                {
                    ["url"] = args["url"],
                    ["waitForSelector"] = args["wait_for_selector"]
                });

            // Sessions
            case "create_session":
                return await CallBrokerApi("POST", "/sessions", new JObject
                {
                    ["url"] = args["url"],
                    ["ttlMinutes"] = args["ttl_minutes"]
                });

            case "get_session":
                return await CallBrokerApi("GET", $"/sessions/{args["session_id"]}");

            case "close_session":
                await CallBrokerApi("DELETE", $"/sessions/{args["session_id"]}");
                return new JObject { ["success"] = true, ["closed"] = args["session_id"] };

            case "navigate":
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/navigate", new JObject
                {
                    ["url"] = args["url"]
                });

            case "list_session_tools":
                return await CallBrokerApi("GET", $"/sessions/{args["session_id"]}/tools");

            // Execution
            case "call_tool":
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/tools/{args["tool_name"]}/call", new JObject
                {
                    ["input"] = args["input"]
                });

            case "execute_stateless":
                return await CallBrokerApi("POST", "/execute", new JObject
                {
                    ["url"] = args["url"],
                    ["toolName"] = args["tool_name"],
                    ["input"] = args["input"]
                });

            // Authentication
            case "inject_auth":
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/authenticate", new JObject
                {
                    ["cookies"] = args["cookies"],
                    ["localStorage"] = args["local_storage"],
                    ["headers"] = args["headers"]
                });

            // Screenshot
            case "take_screenshot":
                var fullPage = args["full_page"]?.ToObject<bool>() ?? false;
                return await CallBrokerApi("GET", $"/sessions/{args["session_id"]}/screenshot?fullPage={fullPage}");

            // Direct Playwright tools (convenience wrappers)
            case "browser_click":
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/tools/browser_click/call", new JObject
                {
                    ["input"] = new JObject { ["selector"] = args["selector"] }
                });

            case "browser_type":
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/tools/browser_type/call", new JObject
                {
                    ["input"] = new JObject
                    {
                        ["selector"] = args["selector"],
                        ["text"] = args["text"],
                        ["submit"] = args["submit"]
                    }
                });

            case "browser_get_text":
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/tools/browser_get_text/call", new JObject
                {
                    ["input"] = new JObject { ["selector"] = args["selector"] }
                });

            case "browser_wait":
                var waitInput = new JObject();
                if (args["selector"] != null) waitInput["selector"] = args["selector"];
                if (args["timeout_ms"] != null) waitInput["timeout"] = args["timeout_ms"];
                if (args["state"] != null) waitInput["state"] = args["state"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/tools/browser_wait_for_selector/call", new JObject
                {
                    ["input"] = waitInput
                });

            case "browser_select":
                var selectInput = new JObject { ["selector"] = args["selector"] };
                if (args["value"] != null) selectInput["value"] = args["value"];
                if (args["label"] != null) selectInput["label"] = args["label"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/tools/browser_select_option/call", new JObject
                {
                    ["input"] = selectInput
                });

            case "browser_get_page":
                var format = args["format"]?.ToString() ?? "text";
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/tools/browser_get_page_content/call", new JObject
                {
                    ["input"] = new JObject { ["format"] = format }
                });

            case "browser_evaluate":
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/tools/browser_evaluate/call", new JObject
                {
                    ["input"] = new JObject { ["script"] = args["script"] }
                });

            case "browser_scroll":
                var scrollInput = new JObject();
                if (args["direction"] != null) scrollInput["direction"] = args["direction"];
                if (args["amount"] != null) scrollInput["amount"] = args["amount"];
                if (args["selector"] != null) scrollInput["selector"] = args["selector"];
                return await CallBrokerApi("POST", $"/sessions/{args["session_id"]}/tools/browser_scroll/call", new JObject
                {
                    ["input"] = scrollInput
                });

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private async Task<JObject> CallBrokerApi(string method, string path, JObject body = null)
    {
        // The broker URL is set dynamically via policy template
        var brokerUrl = Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var fullPath = $"{brokerUrl}/api{path}";

        var request = new HttpRequestMessage(new HttpMethod(method), fullPath);

        // Copy API key header
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
        {
            throw new HttpRequestException($"Broker API returned {(int)response.StatusCode}: {content}");
        }

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
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
        }
        catch { }
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
