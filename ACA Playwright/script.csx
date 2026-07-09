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
    // ─── Configuration ────────────────────────────────────────────────────────────
    private const string ACA_SUBSCRIPTION_ID = "[[YOUR_SUBSCRIPTION_ID]]";
    private const string ACA_RESOURCE_GROUP = "[[YOUR_RESOURCE_GROUP]]";
    private const string ACA_SANDBOX_GROUP = "[[YOUR_SANDBOX_GROUP]]";
    private const string ACA_REGION = "[[YOUR_REGION]]";
    private const string ACA_DATA_PLANE_BASE = "https://management.[[YOUR_REGION]].azuredevcompute.io";
    private const string ACA_DISK_IMAGE = "[[YOUR_DISK_IMAGE_GUID]]";
    private const bool ACA_DISK_IS_PUBLIC = false;
    private const int MAX_OUTPUT_BYTES = 8192;
    private const int DEFAULT_TIMEOUT_SECONDS = 60;
    private const string CONNECTOR_LABEL = "aca-playwright";

    // ─── App Insights ─────────────────────────────────────────────────────────────
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    // ─── Paths ────────────────────────────────────────────────────────────────────
    private const string STATE_FILE = "/workspace/.browser-state.json";
    private const string CONSOLE_FILE = "/workspace/.console-log.json";
    private const string CONFIG_FILE = "/workspace/.session-config.json";
    private const string WORKSPACE_DIR = "/workspace";

    // ─── MCP Tool Definitions ─────────────────────────────────────────────────────
    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "navigate",
            ["description"] = "Navigate the browser to a URL. Returns page title, final URL (after redirects), HTTP status, and load time. Browser state (cookies, localStorage) persists across calls within the same session.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID. If omitted, a new session is created automatically." },
                    ["url"] = new JObject { ["type"] = "string", ["description"] = "The URL to navigate to." },
                    ["wait_until"] = new JObject { ["type"] = "string", ["description"] = "When to consider navigation complete: load, domcontentloaded, networkidle, commit. Default: domcontentloaded.", ["enum"] = new JArray { "load", "domcontentloaded", "networkidle", "commit" } },
                    ["wait_for_selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector to wait for after navigation." },
                    ["timeout"] = new JObject { ["type"] = "integer", ["description"] = "Timeout in seconds. Default 30." }
                },
                ["required"] = new JArray { "url" }
            }
        },
        new JObject
        {
            ["name"] = "screenshot",
            ["description"] = "Capture a screenshot of the current page or a specific element. Returns base64-encoded PNG/JPEG. Also saves to /workspace for later download.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector of element to screenshot. Omit for viewport/full page." },
                    ["full_page"] = new JObject { ["type"] = "boolean", ["description"] = "Capture the entire scrollable page. Default: false." },
                    ["format"] = new JObject { ["type"] = "string", ["description"] = "Image format: png or jpeg. Default: png.", ["enum"] = new JArray { "png", "jpeg" } },
                    ["quality"] = new JObject { ["type"] = "integer", ["description"] = "JPEG quality 0-100 (only for jpeg format)." }
                },
                ["required"] = new JArray { "session_id" }
            }
        },
        new JObject
        {
            ["name"] = "click",
            ["description"] = "Click an element on the page by CSS selector, XPath, or Playwright locator (e.g., 'text=Submit', 'role=button[name=\"Save\"]'). Waits for the element to be actionable before clicking.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "Element selector to click." },
                    ["button"] = new JObject { ["type"] = "string", ["description"] = "Mouse button: left, right, middle. Default: left.", ["enum"] = new JArray { "left", "right", "middle" } },
                    ["click_count"] = new JObject { ["type"] = "integer", ["description"] = "Click count (2 for double-click). Default: 1." },
                    ["wait_after"] = new JObject { ["type"] = "string", ["description"] = "Wait condition after click: 'navigation', 'networkidle', or a CSS selector to wait for." }
                },
                ["required"] = new JArray { "session_id", "selector" }
            }
        },
        new JObject
        {
            ["name"] = "fill",
            ["description"] = "Fill one or more form fields. Each field is specified by a selector and value pair. Clears existing content before typing.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["fields"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Array of {selector, value} objects.",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["selector"] = new JObject { ["type"] = "string" },
                                ["value"] = new JObject { ["type"] = "string" }
                            },
                            ["required"] = new JArray { "selector", "value" }
                        }
                    }
                },
                ["required"] = new JArray { "session_id", "fields" }
            }
        },
        new JObject
        {
            ["name"] = "extract",
            ["description"] = "Extract text, HTML, or attributes from elements matching a selector. Useful for scraping data from the current page.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["selector"] = new JObject { ["type"] = "string", ["description"] = "CSS selector or XPath." },
                    ["extract"] = new JObject { ["type"] = "string", ["description"] = "What to extract: textContent, innerText, innerHTML, outerHTML, attribute. Default: textContent.", ["enum"] = new JArray { "textContent", "innerText", "innerHTML", "outerHTML", "attribute" } },
                    ["attribute"] = new JObject { ["type"] = "string", ["description"] = "Attribute name (only when extract=attribute)." },
                    ["all"] = new JObject { ["type"] = "boolean", ["description"] = "Extract from all matches (true) or just the first (false). Default: false." }
                },
                ["required"] = new JArray { "session_id", "selector" }
            }
        },
        new JObject
        {
            ["name"] = "run_script",
            ["description"] = "Execute a custom Playwright script. The script has access to 'page' (current page) and 'context' (browser context). Output the result via console.log(JSON.stringify(data)). Supports full Playwright API.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["script"] = new JObject { ["type"] = "string", ["description"] = "Playwright script (JavaScript). Has access to 'page' and 'context'." },
                    ["timeout"] = new JObject { ["type"] = "integer", ["description"] = "Timeout in seconds. Default 60." }
                },
                ["required"] = new JArray { "session_id", "script" }
            }
        },
        new JObject
        {
            ["name"] = "generate_pdf",
            ["description"] = "Export the current page as a PDF document. Works only with Chromium. Returns base64-encoded PDF.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["format"] = new JObject { ["type"] = "string", ["description"] = "Paper format: Letter, Legal, Tabloid, A3, A4, A5. Default: Letter.", ["enum"] = new JArray { "Letter", "Legal", "Tabloid", "A3", "A4", "A5" } },
                    ["landscape"] = new JObject { ["type"] = "boolean", ["description"] = "Landscape orientation. Default: false." },
                    ["print_background"] = new JObject { ["type"] = "boolean", ["description"] = "Include background graphics. Default: true." }
                },
                ["required"] = new JArray { "session_id" }
            }
        },
        new JObject
        {
            ["name"] = "get_console_log",
            ["description"] = "Retrieve browser console messages captured during the session (log, warn, error, info).",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." }
                },
                ["required"] = new JArray { "session_id" }
            }
        },
        new JObject
        {
            ["name"] = "download_artifact",
            ["description"] = "Download a file from the sandbox workspace (screenshots, PDFs, HAR files, etc.).",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID." },
                    ["file_path"] = new JObject { ["type"] = "string", ["description"] = "Full path in sandbox (e.g., /workspace/screenshot.png)." }
                },
                ["required"] = new JArray { "session_id", "file_path" }
            }
        },
        new JObject
        {
            ["name"] = "create_session",
            ["description"] = "Create a new browser session with custom viewport, user agent, or locale. Sessions auto-suspend after 10 minutes idle.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["cpu"] = new JObject { ["type"] = "string", ["description"] = "CPU: 2000m or 4000m. Default: 2000m." },
                    ["memory"] = new JObject { ["type"] = "string", ["description"] = "Memory: 4096Mi or 8192Mi. Default: 4096Mi." },
                    ["viewport"] = new JObject { ["type"] = "string", ["description"] = "Viewport as WxH (e.g., 1920x1080). Default: 1280x720." },
                    ["user_agent"] = new JObject { ["type"] = "string", ["description"] = "Custom User-Agent string." },
                    ["locale"] = new JObject { ["type"] = "string", ["description"] = "Browser locale (e.g., en-US). Default: en-US." },
                    ["record_har"] = new JObject { ["type"] = "boolean", ["description"] = "Record a HAR file of all network traffic." }
                },
                ["required"] = new JArray {}
            }
        },
        new JObject
        {
            ["name"] = "destroy_session",
            ["description"] = "Destroy a browser sandbox session and free all resources. Irreversible.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["session_id"] = new JObject { ["type"] = "string", ["description"] = "Session ID to destroy." }
                },
                ["required"] = new JArray { "session_id" }
            }
        }
    };

    // ─── Main Router ──────────────────────────────────────────────────────────────
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;
        try
        {
            switch (operationId)
            {
                case "InvokeMCP":
                    return await HandleMcpRequestAsync().ConfigureAwait(false);
                case "CreateSession":
                    return await HandleCreateSessionAsync().ConfigureAwait(false);
                case "NavigateToUrl":
                    return await HandleNavigateAsync().ConfigureAwait(false);
                case "TakeScreenshot":
                    return await HandleScreenshotAsync().ConfigureAwait(false);
                case "ClickElement":
                    return await HandleClickAsync().ConfigureAwait(false);
                case "FillForm":
                    return await HandleFillAsync().ConfigureAwait(false);
                case "ExtractContent":
                    return await HandleExtractAsync().ConfigureAwait(false);
                case "RunScript":
                    return await HandleRunScriptAsync().ConfigureAwait(false);
                case "GeneratePdf":
                    return await HandlePdfAsync().ConfigureAwait(false);
                case "GetConsoleLog":
                    return await HandleConsoleLogAsync().ConfigureAwait(false);
                case "DownloadArtifact":
                    return await HandleDownloadArtifactAsync().ConfigureAwait(false);
                case "DestroySession":
                    return await HandleDestroySessionAsync().ConfigureAwait(false);
                default:
                    return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = $"Unknown operation: {operationId}" });
            }
        }
        catch (Exception ex)
        {
            LogToAppInsights("Error", new Dictionary<string, string>
            {
                ["operation"] = operationId,
                ["error"] = ex.Message,
                ["stackTrace"] = ex.StackTrace
            });
            return CreateJsonResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message,
                ["operation"] = operationId
            });
        }
    }

    // ─── Typed Operation Handlers ─────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleCreateSessionAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var cpu = body.Value<string>("cpu") ?? "2000m";
        var memory = body.Value<string>("memory") ?? "4096Mi";
        var viewport = body.Value<string>("viewport") ?? "1280x720";
        var userAgent = body.Value<string>("userAgent") ?? "";
        var locale = body.Value<string>("locale") ?? "en-US";
        var recordHar = body.Value<bool?>("recordHar") ?? false;

        var sandbox = await CreateSandboxAsync("", cpu, memory, 10).ConfigureAwait(false);
        var sessionId = sandbox.Value<string>("id") ?? sandbox.Value<string>("name") ?? Guid.NewGuid().ToString();

        // Write session config to sandbox for Playwright scripts to read
        var config = new JObject
        {
            ["viewport"] = viewport,
            ["userAgent"] = userAgent,
            ["locale"] = locale,
            ["recordHar"] = recordHar
        };
        await WriteConfigToSandboxAsync(sessionId, config).ConfigureAwait(false);

        LogToAppInsights("SessionCreated", new Dictionary<string, string>
        {
            ["sessionId"] = sessionId,
            ["cpu"] = cpu,
            ["memory"] = memory,
            ["viewport"] = viewport
        });

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["sessionId"] = sessionId,
            ["state"] = "Running",
            ["cpu"] = cpu,
            ["memory"] = memory,
            ["viewport"] = viewport,
            ["createdAt"] = DateTime.UtcNow.ToString("O")
        });
    }

    private async Task<HttpResponseMessage> HandleNavigateAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var url = body.Value<string>("url");
        var waitUntil = body.Value<string>("waitUntil") ?? "domcontentloaded";
        var waitForSelector = body.Value<string>("waitForSelector") ?? "";
        var timeout = body.Value<int?>("timeout") ?? DEFAULT_TIMEOUT_SECONDS;

        if (string.IsNullOrWhiteSpace(sessionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId is required." });
        if (string.IsNullOrWhiteSpace(url))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "url is required." });

        var script = BuildNavigateScript(url, waitUntil, waitForSelector, timeout);
        var startTime = DateTime.UtcNow;
        var result = await RunPlaywrightInSandboxAsync(sessionId, script, timeout + 10).ConfigureAwait(false);
        var elapsed = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        var output = ParseScriptOutput(result);
        output["sessionId"] = sessionId;
        output["loadTimeMs"] = elapsed;

        return CreateJsonResponse(HttpStatusCode.OK, output);
    }

    private async Task<HttpResponseMessage> HandleScreenshotAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var selector = body.Value<string>("selector") ?? "";
        var fullPage = body.Value<bool?>("fullPage") ?? false;
        var format = body.Value<string>("format") ?? "png";
        var quality = body.Value<int?>("quality");

        if (string.IsNullOrWhiteSpace(sessionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId is required." });

        var fileName = $"screenshot-{DateTime.UtcNow:yyyyMMddHHmmss}.{format}";
        var filePath = $"{WORKSPACE_DIR}/{fileName}";
        var script = BuildScreenshotScript(selector, fullPage, format, quality, filePath);
        await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);

        // Read the screenshot file back
        var fileData = await ReadFileFromSandboxAsync(sessionId, filePath).ConfigureAwait(false);

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["sessionId"] = sessionId,
            ["content"] = fileData.Value<string>("content"),
            ["mimeType"] = format == "jpeg" ? "image/jpeg" : "image/png",
            ["sizeBytes"] = fileData.Value<long>("sizeBytes"),
            ["filePath"] = filePath
        });
    }

    private async Task<HttpResponseMessage> HandleClickAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var selector = body.Value<string>("selector");
        var button = body.Value<string>("button") ?? "left";
        var clickCount = body.Value<int?>("clickCount") ?? 1;
        var waitAfter = body.Value<string>("waitAfter") ?? "";

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(selector))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId and selector are required." });

        var script = BuildClickScript(selector, button, clickCount, waitAfter);
        var result = await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);
        var output = ParseScriptOutput(result);
        output["sessionId"] = sessionId;
        output["success"] = true;

        return CreateJsonResponse(HttpStatusCode.OK, output);
    }

    private async Task<HttpResponseMessage> HandleFillAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var fields = body["fields"] as JArray;

        if (string.IsNullOrWhiteSpace(sessionId) || fields == null || fields.Count == 0)
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId and fields are required." });

        var script = BuildFillScript(fields);
        var result = await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);
        var output = ParseScriptOutput(result);
        output["sessionId"] = sessionId;
        output["success"] = true;

        return CreateJsonResponse(HttpStatusCode.OK, output);
    }

    private async Task<HttpResponseMessage> HandleExtractAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var selector = body.Value<string>("selector");
        var extract = body.Value<string>("extract") ?? "textContent";
        var attribute = body.Value<string>("attribute") ?? "";
        var all = body.Value<bool?>("all") ?? false;

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(selector))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId and selector are required." });

        var script = BuildExtractScript(selector, extract, attribute, all);
        var result = await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);
        var output = ParseScriptOutput(result);
        output["sessionId"] = sessionId;
        output["selector"] = selector;

        return CreateJsonResponse(HttpStatusCode.OK, output);
    }

    private async Task<HttpResponseMessage> HandleRunScriptAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var userScript = body.Value<string>("script");
        var timeout = body.Value<int?>("timeout") ?? DEFAULT_TIMEOUT_SECONDS;

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(userScript))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId and script are required." });

        var wrappedScript = BuildCustomScript(userScript);
        var startTime = DateTime.UtcNow;
        var result = await RunPlaywrightInSandboxAsync(sessionId, wrappedScript, timeout + 10).ConfigureAwait(false);
        var elapsed = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        var stdout = result.Value<string>("stdout") ?? "";
        var stderr = result.Value<string>("stderr") ?? "";
        if (stdout.Length > MAX_OUTPUT_BYTES) stdout = stdout.Substring(0, MAX_OUTPUT_BYTES) + "\n[truncated]";
        if (stderr.Length > MAX_OUTPUT_BYTES) stderr = stderr.Substring(0, MAX_OUTPUT_BYTES) + "\n[truncated]";

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["sessionId"] = sessionId,
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["exitCode"] = result.Value<int?>("exitCode") ?? -1,
            ["executionTimeMs"] = elapsed
        });
    }

    private async Task<HttpResponseMessage> HandlePdfAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var format = body.Value<string>("format") ?? "Letter";
        var landscape = body.Value<bool?>("landscape") ?? false;
        var printBackground = body.Value<bool?>("printBackground") ?? true;

        if (string.IsNullOrWhiteSpace(sessionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId is required." });

        var fileName = $"page-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        var filePath = $"{WORKSPACE_DIR}/{fileName}";
        var script = BuildPdfScript(format, landscape, printBackground, filePath);
        await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);

        var fileData = await ReadFileFromSandboxAsync(sessionId, filePath).ConfigureAwait(false);

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["sessionId"] = sessionId,
            ["content"] = fileData.Value<string>("content"),
            ["sizeBytes"] = fileData.Value<long>("sizeBytes"),
            ["filePath"] = filePath
        });
    }

    private async Task<HttpResponseMessage> HandleConsoleLogAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");

        if (string.IsNullOrWhiteSpace(sessionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId is required." });

        // Read console log file from sandbox
        try
        {
            var fileData = await ReadFileFromSandboxAsync(sessionId, CONSOLE_FILE).ConfigureAwait(false);
            var content = Encoding.UTF8.GetString(Convert.FromBase64String(fileData.Value<string>("content")));
            var messages = JArray.Parse(content);

            return CreateJsonResponse(HttpStatusCode.OK, new JObject
            {
                ["sessionId"] = sessionId,
                ["messages"] = messages
            });
        }
        catch
        {
            return CreateJsonResponse(HttpStatusCode.OK, new JObject
            {
                ["sessionId"] = sessionId,
                ["messages"] = new JArray()
            });
        }
    }

    private async Task<HttpResponseMessage> HandleDownloadArtifactAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");
        var filePath = body.Value<string>("filePath");

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(filePath))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId and filePath are required." });

        var result = await ReadFileFromSandboxAsync(sessionId, filePath).ConfigureAwait(false);
        return CreateJsonResponse(HttpStatusCode.OK, result);
    }

    private async Task<HttpResponseMessage> HandleDestroySessionAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var sessionId = body.Value<string>("sessionId");

        if (string.IsNullOrWhiteSpace(sessionId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "sessionId is required." });

        await DeleteSandboxAsync(sessionId).ConfigureAwait(false);

        LogToAppInsights("SessionDestroyed", new Dictionary<string, string> { ["sessionId"] = sessionId });

        return CreateJsonResponse(HttpStatusCode.OK, new JObject
        {
            ["success"] = true,
            ["sessionId"] = sessionId
        });
    }

    // ─── MCP Handler ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var request = JObject.Parse(body);
        var method = request.Value<string>("method") ?? "";
        var id = request["id"];

        switch (method)
        {
            case "initialize":
                return McpSuccess(id, new JObject
                {
                    ["protocolVersion"] = "2025-11-25",
                    ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JObject { ["name"] = "aca-playwright", ["version"] = "1.0.0" }
                });

            case "notifications/initialized":
                return McpSuccess(id, new JObject());

            case "tools/list":
                return McpSuccess(id, new JObject { ["tools"] = AVAILABLE_TOOLS });

            case "tools/call":
                return await HandleMcpToolCallAsync(id, request["params"] as JObject).ConfigureAwait(false);

            default:
                return McpError(id, -32601, "Method not found", method);
        }
    }

    private async Task<HttpResponseMessage> HandleMcpToolCallAsync(JToken id, JObject paramsObj)
    {
        var toolName = paramsObj?.Value<string>("name");
        var args = paramsObj?["arguments"] as JObject ?? new JObject();

        try
        {
            JToken result;
            switch (toolName)
            {
                case "navigate":
                    result = await McpNavigateAsync(args).ConfigureAwait(false);
                    break;
                case "screenshot":
                    result = await McpScreenshotAsync(args).ConfigureAwait(false);
                    break;
                case "click":
                    result = await McpClickAsync(args).ConfigureAwait(false);
                    break;
                case "fill":
                    result = await McpFillAsync(args).ConfigureAwait(false);
                    break;
                case "extract":
                    result = await McpExtractAsync(args).ConfigureAwait(false);
                    break;
                case "run_script":
                    result = await McpRunScriptAsync(args).ConfigureAwait(false);
                    break;
                case "generate_pdf":
                    result = await McpGeneratePdfAsync(args).ConfigureAwait(false);
                    break;
                case "get_console_log":
                    result = await McpGetConsoleLogAsync(args).ConfigureAwait(false);
                    break;
                case "download_artifact":
                    result = await McpDownloadArtifactAsync(args).ConfigureAwait(false);
                    break;
                case "create_session":
                    result = await McpCreateSessionAsync(args).ConfigureAwait(false);
                    break;
                case "destroy_session":
                    result = await McpDestroySessionAsync(args).ConfigureAwait(false);
                    break;
                default:
                    return McpError(id, -32602, $"Unknown tool: {toolName}");
            }

            return McpSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString(Newtonsoft.Json.Formatting.None) } },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return McpSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Error: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    // ─── MCP Tool Implementations ─────────────────────────────────────────────────

    private async Task<JObject> McpNavigateAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var url = args.Value<string>("url");
        var waitUntil = args.Value<string>("wait_until") ?? "domcontentloaded";
        var waitForSelector = args.Value<string>("wait_for_selector") ?? "";
        var timeout = args.Value<int?>("timeout") ?? 30;

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = await AutoCreateSessionAsync().ConfigureAwait(false);
        }

        var script = BuildNavigateScript(url, waitUntil, waitForSelector, timeout);
        var result = await RunPlaywrightInSandboxAsync(sessionId, script, timeout + 10).ConfigureAwait(false);
        var output = ParseScriptOutput(result);
        output["session_id"] = sessionId;
        return output;
    }

    private async Task<JObject> McpScreenshotAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var selector = args.Value<string>("selector") ?? "";
        var fullPage = args.Value<bool?>("full_page") ?? false;
        var format = args.Value<string>("format") ?? "png";
        var quality = args.Value<int?>("quality");

        var fileName = $"screenshot-{DateTime.UtcNow:yyyyMMddHHmmss}.{format}";
        var filePath = $"{WORKSPACE_DIR}/{fileName}";
        var script = BuildScreenshotScript(selector, fullPage, format, quality, filePath);
        await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);

        var fileData = await ReadFileFromSandboxAsync(sessionId, filePath).ConfigureAwait(false);
        return new JObject
        {
            ["session_id"] = sessionId,
            ["file_path"] = filePath,
            ["mime_type"] = format == "jpeg" ? "image/jpeg" : "image/png",
            ["size_bytes"] = fileData.Value<long>("sizeBytes"),
            ["content"] = fileData.Value<string>("content")
        };
    }

    private async Task<JObject> McpClickAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var selector = args.Value<string>("selector");
        var button = args.Value<string>("button") ?? "left";
        var clickCount = args.Value<int?>("click_count") ?? 1;
        var waitAfter = args.Value<string>("wait_after") ?? "";

        var script = BuildClickScript(selector, button, clickCount, waitAfter);
        var result = await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);
        var output = ParseScriptOutput(result);
        output["session_id"] = sessionId;
        output["success"] = true;
        return output;
    }

    private async Task<JObject> McpFillAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var fields = args["fields"] as JArray ?? new JArray();

        var script = BuildFillScript(fields);
        var result = await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);
        var output = ParseScriptOutput(result);
        output["session_id"] = sessionId;
        output["success"] = true;
        return output;
    }

    private async Task<JObject> McpExtractAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var selector = args.Value<string>("selector");
        var extract = args.Value<string>("extract") ?? "textContent";
        var attribute = args.Value<string>("attribute") ?? "";
        var all = args.Value<bool?>("all") ?? false;

        var script = BuildExtractScript(selector, extract, attribute, all);
        var result = await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);
        var output = ParseScriptOutput(result);
        output["session_id"] = sessionId;
        output["selector"] = selector;
        return output;
    }

    private async Task<JObject> McpRunScriptAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var userScript = args.Value<string>("script");
        var timeout = args.Value<int?>("timeout") ?? DEFAULT_TIMEOUT_SECONDS;

        var wrappedScript = BuildCustomScript(userScript);
        var startTime = DateTime.UtcNow;
        var result = await RunPlaywrightInSandboxAsync(sessionId, wrappedScript, timeout + 10).ConfigureAwait(false);
        var elapsed = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        var stdout = result.Value<string>("stdout") ?? "";
        var stderr = result.Value<string>("stderr") ?? "";
        if (stdout.Length > MAX_OUTPUT_BYTES) stdout = stdout.Substring(0, MAX_OUTPUT_BYTES) + "\n[truncated]";
        if (stderr.Length > MAX_OUTPUT_BYTES) stderr = stderr.Substring(0, MAX_OUTPUT_BYTES) + "\n[truncated]";

        return new JObject
        {
            ["session_id"] = sessionId,
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["exit_code"] = result.Value<int?>("exitCode") ?? -1,
            ["execution_time_ms"] = elapsed
        };
    }

    private async Task<JObject> McpGeneratePdfAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var format = args.Value<string>("format") ?? "Letter";
        var landscape = args.Value<bool?>("landscape") ?? false;
        var printBackground = args.Value<bool?>("print_background") ?? true;

        var fileName = $"page-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        var filePath = $"{WORKSPACE_DIR}/{fileName}";
        var script = BuildPdfScript(format, landscape, printBackground, filePath);
        await RunPlaywrightInSandboxAsync(sessionId, script, DEFAULT_TIMEOUT_SECONDS).ConfigureAwait(false);

        var fileData = await ReadFileFromSandboxAsync(sessionId, filePath).ConfigureAwait(false);
        return new JObject
        {
            ["session_id"] = sessionId,
            ["file_path"] = filePath,
            ["size_bytes"] = fileData.Value<long>("sizeBytes"),
            ["content"] = fileData.Value<string>("content")
        };
    }

    private async Task<JObject> McpGetConsoleLogAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        try
        {
            var fileData = await ReadFileFromSandboxAsync(sessionId, CONSOLE_FILE).ConfigureAwait(false);
            var content = Encoding.UTF8.GetString(Convert.FromBase64String(fileData.Value<string>("content")));
            var messages = JArray.Parse(content);
            return new JObject { ["session_id"] = sessionId, ["messages"] = messages };
        }
        catch
        {
            return new JObject { ["session_id"] = sessionId, ["messages"] = new JArray() };
        }
    }

    private async Task<JObject> McpDownloadArtifactAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        var filePath = args.Value<string>("file_path");
        return await ReadFileFromSandboxAsync(sessionId, filePath).ConfigureAwait(false);
    }

    private async Task<JObject> McpCreateSessionAsync(JObject args)
    {
        var cpu = args.Value<string>("cpu") ?? "2000m";
        var memory = args.Value<string>("memory") ?? "4096Mi";
        var viewport = args.Value<string>("viewport") ?? "1280x720";
        var userAgent = args.Value<string>("user_agent") ?? "";
        var locale = args.Value<string>("locale") ?? "en-US";
        var recordHar = args.Value<bool?>("record_har") ?? false;

        var sandbox = await CreateSandboxAsync("", cpu, memory, 10).ConfigureAwait(false);
        var sessionId = sandbox.Value<string>("id") ?? sandbox.Value<string>("name") ?? Guid.NewGuid().ToString();

        var config = new JObject
        {
            ["viewport"] = viewport,
            ["userAgent"] = userAgent,
            ["locale"] = locale,
            ["recordHar"] = recordHar
        };
        await WriteConfigToSandboxAsync(sessionId, config).ConfigureAwait(false);

        return new JObject
        {
            ["session_id"] = sessionId,
            ["state"] = "Running",
            ["cpu"] = cpu,
            ["memory"] = memory,
            ["viewport"] = viewport
        };
    }

    private async Task<JObject> McpDestroySessionAsync(JObject args)
    {
        var sessionId = args.Value<string>("session_id");
        await DeleteSandboxAsync(sessionId).ConfigureAwait(false);
        return new JObject { ["success"] = true, ["session_id"] = sessionId };
    }

    // ─── Playwright Script Builders ───────────────────────────────────────────────

    private string BuildNavigateScript(string url, string waitUntil, string waitForSelector, int timeout)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const { chromium } = require('playwright');");
        sb.AppendLine("const fs = require('fs');");
        sb.AppendLine("(async () => {");
        sb.AppendLine("  const config = fs.existsSync('/workspace/.session-config.json') ? JSON.parse(fs.readFileSync('/workspace/.session-config.json','utf8')) : {};");
        sb.AppendLine("  const [w,h] = (config.viewport||'1280x720').split('x').map(Number);");
        sb.AppendLine("  const browser = await chromium.launch({ headless: true });");
        sb.AppendLine("  const ctxOpts = { viewport: {width:w, height:h}, locale: config.locale||'en-US', ignoreHTTPSErrors: true };");
        sb.AppendLine("  if (config.userAgent) ctxOpts.userAgent = config.userAgent;");
        sb.AppendLine("  if (fs.existsSync('/workspace/.browser-state.json')) ctxOpts.storageState = '/workspace/.browser-state.json';");
        sb.AppendLine("  if (config.recordHar) ctxOpts.recordHar = { path: '/workspace/trace.har' };");
        sb.AppendLine("  const context = await browser.newContext(ctxOpts);");
        sb.AppendLine("  const page = await context.newPage();");
        sb.AppendLine("  const consoleMessages = [];");
        sb.AppendLine("  page.on('console', msg => consoleMessages.push({type:msg.type(),text:msg.text()}));");
        sb.AppendLine($"  const response = await page.goto({JsonString(url)}, {{ waitUntil: {JsonString(waitUntil)}, timeout: {timeout * 1000} }});");
        if (!string.IsNullOrWhiteSpace(waitForSelector))
        {
            sb.AppendLine($"  await page.waitForSelector({JsonString(waitForSelector)}, {{ timeout: {timeout * 1000} }});");
        }
        sb.AppendLine("  const result = { url: page.url(), title: await page.title(), status: response ? response.status() : 0 };");
        sb.AppendLine("  await context.storageState({ path: '/workspace/.browser-state.json' });");
        sb.AppendLine("  fs.writeFileSync('/workspace/.last-url.txt', page.url());");
        sb.AppendLine("  fs.writeFileSync('/workspace/.console-log.json', JSON.stringify(consoleMessages));");
        sb.AppendLine("  await browser.close();");
        sb.AppendLine("  console.log(JSON.stringify(result));");
        sb.AppendLine("})().catch(e => { console.error(e.message); process.exit(1); });");
        return sb.ToString();
    }

    private string BuildScreenshotScript(string selector, bool fullPage, string format, int? quality, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const { chromium } = require('playwright');");
        sb.AppendLine("const fs = require('fs');");
        sb.AppendLine("(async () => {");
        sb.AppendLine("  const config = fs.existsSync('/workspace/.session-config.json') ? JSON.parse(fs.readFileSync('/workspace/.session-config.json','utf8')) : {};");
        sb.AppendLine("  const [w,h] = (config.viewport||'1280x720').split('x').map(Number);");
        sb.AppendLine("  const browser = await chromium.launch({ headless: true });");
        sb.AppendLine("  const ctxOpts = { viewport: {width:w, height:h}, locale: config.locale||'en-US', ignoreHTTPSErrors: true };");
        sb.AppendLine("  if (config.userAgent) ctxOpts.userAgent = config.userAgent;");
        sb.AppendLine("  if (fs.existsSync('/workspace/.browser-state.json')) ctxOpts.storageState = '/workspace/.browser-state.json';");
        sb.AppendLine("  const context = await browser.newContext(ctxOpts);");
        sb.AppendLine("  const page = await context.newPage();");
        // Navigate to current state — reload last URL from state
        sb.AppendLine("  if (fs.existsSync('/workspace/.last-url.txt')) { await page.goto(fs.readFileSync('/workspace/.last-url.txt','utf8').trim(), {waitUntil:'domcontentloaded'}); }");

        var ssOpts = new StringBuilder("{ path: " + JsonString(filePath) + $", type: {JsonString(format)}");
        if (fullPage) ssOpts.Append(", fullPage: true");
        if (quality.HasValue && format == "jpeg") ssOpts.Append($", quality: {quality.Value}");
        ssOpts.Append(" }");

        if (!string.IsNullOrWhiteSpace(selector))
        {
            sb.AppendLine($"  const el = await page.locator({JsonString(selector)}).first();");
            sb.AppendLine($"  await el.screenshot({ssOpts});");
        }
        else
        {
            sb.AppendLine($"  await page.screenshot({ssOpts});");
        }
        sb.AppendLine("  await context.storageState({ path: '/workspace/.browser-state.json' });");
        sb.AppendLine("  await browser.close();");
        sb.AppendLine("  console.log(JSON.stringify({success:true}));");
        sb.AppendLine("})().catch(e => { console.error(e.message); process.exit(1); });");
        return sb.ToString();
    }

    private string BuildClickScript(string selector, string button, int clickCount, string waitAfter)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const { chromium } = require('playwright');");
        sb.AppendLine("const fs = require('fs');");
        sb.AppendLine("(async () => {");
        sb.AppendLine("  const config = fs.existsSync('/workspace/.session-config.json') ? JSON.parse(fs.readFileSync('/workspace/.session-config.json','utf8')) : {};");
        sb.AppendLine("  const [w,h] = (config.viewport||'1280x720').split('x').map(Number);");
        sb.AppendLine("  const browser = await chromium.launch({ headless: true });");
        sb.AppendLine("  const ctxOpts = { viewport: {width:w, height:h}, locale: config.locale||'en-US', ignoreHTTPSErrors: true };");
        sb.AppendLine("  if (config.userAgent) ctxOpts.userAgent = config.userAgent;");
        sb.AppendLine("  if (fs.existsSync('/workspace/.browser-state.json')) ctxOpts.storageState = '/workspace/.browser-state.json';");
        sb.AppendLine("  const context = await browser.newContext(ctxOpts);");
        sb.AppendLine("  const page = await context.newPage();");
        sb.AppendLine("  const consoleMessages = [];");
        sb.AppendLine("  page.on('console', msg => consoleMessages.push({type:msg.type(),text:msg.text()}));");
        sb.AppendLine("  if (fs.existsSync('/workspace/.last-url.txt')) { await page.goto(fs.readFileSync('/workspace/.last-url.txt','utf8').trim(), {waitUntil:'domcontentloaded'}); }");

        if (waitAfter == "navigation")
        {
            sb.AppendLine($"  await Promise.all([page.waitForNavigation(), page.locator({JsonString(selector)}).click({{ button: {JsonString(button)}, clickCount: {clickCount} }})]);");
        }
        else
        {
            sb.AppendLine($"  await page.locator({JsonString(selector)}).click({{ button: {JsonString(button)}, clickCount: {clickCount} }});");
            if (waitAfter == "networkidle")
            {
                sb.AppendLine("  await page.waitForLoadState('networkidle');");
            }
            else if (!string.IsNullOrWhiteSpace(waitAfter))
            {
                sb.AppendLine($"  await page.waitForSelector({JsonString(waitAfter)});");
            }
        }

        sb.AppendLine("  fs.writeFileSync('/workspace/.last-url.txt', page.url());");
        sb.AppendLine("  const result = { url: page.url(), title: await page.title() };");
        sb.AppendLine("  await context.storageState({ path: '/workspace/.browser-state.json' });");
        sb.AppendLine("  fs.writeFileSync('/workspace/.console-log.json', JSON.stringify(consoleMessages));");
        sb.AppendLine("  await browser.close();");
        sb.AppendLine("  console.log(JSON.stringify(result));");
        sb.AppendLine("})().catch(e => { console.error(e.message); process.exit(1); });");
        return sb.ToString();
    }

    private string BuildFillScript(JArray fields)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const { chromium } = require('playwright');");
        sb.AppendLine("const fs = require('fs');");
        sb.AppendLine("(async () => {");
        sb.AppendLine("  const config = fs.existsSync('/workspace/.session-config.json') ? JSON.parse(fs.readFileSync('/workspace/.session-config.json','utf8')) : {};");
        sb.AppendLine("  const [w,h] = (config.viewport||'1280x720').split('x').map(Number);");
        sb.AppendLine("  const browser = await chromium.launch({ headless: true });");
        sb.AppendLine("  const ctxOpts = { viewport: {width:w, height:h}, locale: config.locale||'en-US', ignoreHTTPSErrors: true };");
        sb.AppendLine("  if (config.userAgent) ctxOpts.userAgent = config.userAgent;");
        sb.AppendLine("  if (fs.existsSync('/workspace/.browser-state.json')) ctxOpts.storageState = '/workspace/.browser-state.json';");
        sb.AppendLine("  const context = await browser.newContext(ctxOpts);");
        sb.AppendLine("  const page = await context.newPage();");
        sb.AppendLine("  if (fs.existsSync('/workspace/.last-url.txt')) { await page.goto(fs.readFileSync('/workspace/.last-url.txt','utf8').trim(), {waitUntil:'domcontentloaded'}); }");

        foreach (var field in fields)
        {
            var sel = field.Value<string>("selector");
            var val = field.Value<string>("value");
            sb.AppendLine($"  await page.locator({JsonString(sel)}).fill({JsonString(val)});");
        }

        sb.AppendLine("  fs.writeFileSync('/workspace/.last-url.txt', page.url());");
        sb.AppendLine("  const result = { url: page.url(), title: await page.title(), fieldsFilled: " + fields.Count + " };");
        sb.AppendLine("  await context.storageState({ path: '/workspace/.browser-state.json' });");
        sb.AppendLine("  await browser.close();");
        sb.AppendLine("  console.log(JSON.stringify(result));");
        sb.AppendLine("})().catch(e => { console.error(e.message); process.exit(1); });");
        return sb.ToString();
    }

    private string BuildExtractScript(string selector, string extract, string attribute, bool all)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const { chromium } = require('playwright');");
        sb.AppendLine("const fs = require('fs');");
        sb.AppendLine("(async () => {");
        sb.AppendLine("  const config = fs.existsSync('/workspace/.session-config.json') ? JSON.parse(fs.readFileSync('/workspace/.session-config.json','utf8')) : {};");
        sb.AppendLine("  const [w,h] = (config.viewport||'1280x720').split('x').map(Number);");
        sb.AppendLine("  const browser = await chromium.launch({ headless: true });");
        sb.AppendLine("  const ctxOpts = { viewport: {width:w, height:h}, locale: config.locale||'en-US', ignoreHTTPSErrors: true };");
        sb.AppendLine("  if (config.userAgent) ctxOpts.userAgent = config.userAgent;");
        sb.AppendLine("  if (fs.existsSync('/workspace/.browser-state.json')) ctxOpts.storageState = '/workspace/.browser-state.json';");
        sb.AppendLine("  const context = await browser.newContext(ctxOpts);");
        sb.AppendLine("  const page = await context.newPage();");
        sb.AppendLine("  if (fs.existsSync('/workspace/.last-url.txt')) { await page.goto(fs.readFileSync('/workspace/.last-url.txt','utf8').trim(), {waitUntil:'domcontentloaded'}); }");

        if (all)
        {
            sb.AppendLine($"  const elements = await page.locator({JsonString(selector)}).all();");
            sb.AppendLine("  const results = [];");
            sb.AppendLine("  for (const el of elements) {");
            switch (extract)
            {
                case "textContent":
                    sb.AppendLine("    results.push(await el.textContent() || '');");
                    break;
                case "innerText":
                    sb.AppendLine("    results.push(await el.innerText());");
                    break;
                case "innerHTML":
                    sb.AppendLine("    results.push(await el.innerHTML());");
                    break;
                case "outerHTML":
                    sb.AppendLine("    results.push(await el.evaluate(e => e.outerHTML));");
                    break;
                case "attribute":
                    sb.AppendLine($"    results.push(await el.getAttribute({JsonString(attribute)}) || '');");
                    break;
            }
            sb.AppendLine("  }");
            sb.AppendLine("  console.log(JSON.stringify({ count: results.length, results }));");
        }
        else
        {
            sb.AppendLine($"  const el = page.locator({JsonString(selector)}).first();");
            switch (extract)
            {
                case "textContent":
                    sb.AppendLine("  const value = await el.textContent() || '';");
                    break;
                case "innerText":
                    sb.AppendLine("  const value = await el.innerText();");
                    break;
                case "innerHTML":
                    sb.AppendLine("  const value = await el.innerHTML();");
                    break;
                case "outerHTML":
                    sb.AppendLine("  const value = await el.evaluate(e => e.outerHTML);");
                    break;
                case "attribute":
                    sb.AppendLine($"  const value = await el.getAttribute({JsonString(attribute)}) || '';");
                    break;
                default:
                    sb.AppendLine("  const value = await el.textContent() || '';");
                    break;
            }
            sb.AppendLine("  console.log(JSON.stringify({ count: 1, results: [value] }));");
        }

        sb.AppendLine("  await context.storageState({ path: '/workspace/.browser-state.json' });");
        sb.AppendLine("  await browser.close();");
        sb.AppendLine("})().catch(e => { console.error(e.message); process.exit(1); });");
        return sb.ToString();
    }

    private string BuildPdfScript(string format, bool landscape, bool printBackground, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const { chromium } = require('playwright');");
        sb.AppendLine("const fs = require('fs');");
        sb.AppendLine("(async () => {");
        sb.AppendLine("  const config = fs.existsSync('/workspace/.session-config.json') ? JSON.parse(fs.readFileSync('/workspace/.session-config.json','utf8')) : {};");
        sb.AppendLine("  const [w,h] = (config.viewport||'1280x720').split('x').map(Number);");
        sb.AppendLine("  const browser = await chromium.launch({ headless: true });");
        sb.AppendLine("  const ctxOpts = { viewport: {width:w, height:h}, locale: config.locale||'en-US', ignoreHTTPSErrors: true };");
        sb.AppendLine("  if (config.userAgent) ctxOpts.userAgent = config.userAgent;");
        sb.AppendLine("  if (fs.existsSync('/workspace/.browser-state.json')) ctxOpts.storageState = '/workspace/.browser-state.json';");
        sb.AppendLine("  const context = await browser.newContext(ctxOpts);");
        sb.AppendLine("  const page = await context.newPage();");
        sb.AppendLine("  if (fs.existsSync('/workspace/.last-url.txt')) { await page.goto(fs.readFileSync('/workspace/.last-url.txt','utf8').trim(), {waitUntil:'domcontentloaded'}); }");
        sb.AppendLine($"  await page.pdf({{ path: {JsonString(filePath)}, format: {JsonString(format)}, landscape: {(landscape ? "true" : "false")}, printBackground: {(printBackground ? "true" : "false")} }});");
        sb.AppendLine("  await context.storageState({ path: '/workspace/.browser-state.json' });");
        sb.AppendLine("  await browser.close();");
        sb.AppendLine("  console.log(JSON.stringify({success:true}));");
        sb.AppendLine("})().catch(e => { console.error(e.message); process.exit(1); });");
        return sb.ToString();
    }

    private string BuildCustomScript(string userScript)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const { chromium } = require('playwright');");
        sb.AppendLine("const fs = require('fs');");
        sb.AppendLine("(async () => {");
        sb.AppendLine("  const config = fs.existsSync('/workspace/.session-config.json') ? JSON.parse(fs.readFileSync('/workspace/.session-config.json','utf8')) : {};");
        sb.AppendLine("  const [w,h] = (config.viewport||'1280x720').split('x').map(Number);");
        sb.AppendLine("  const browser = await chromium.launch({ headless: true });");
        sb.AppendLine("  const ctxOpts = { viewport: {width:w, height:h}, locale: config.locale||'en-US', ignoreHTTPSErrors: true };");
        sb.AppendLine("  if (config.userAgent) ctxOpts.userAgent = config.userAgent;");
        sb.AppendLine("  if (fs.existsSync('/workspace/.browser-state.json')) ctxOpts.storageState = '/workspace/.browser-state.json';");
        sb.AppendLine("  if (config.recordHar) ctxOpts.recordHar = { path: '/workspace/trace.har' };");
        sb.AppendLine("  const context = await browser.newContext(ctxOpts);");
        sb.AppendLine("  const page = await context.newPage();");
        sb.AppendLine("  if (fs.existsSync('/workspace/.last-url.txt')) { await page.goto(fs.readFileSync('/workspace/.last-url.txt','utf8').trim(), {waitUntil:'domcontentloaded'}); }");
        sb.AppendLine("  // --- User Script ---");
        sb.AppendLine(userScript);
        sb.AppendLine("  // --- End User Script ---");
        sb.AppendLine("  fs.writeFileSync('/workspace/.last-url.txt', page.url());");
        sb.AppendLine("  await context.storageState({ path: '/workspace/.browser-state.json' });");
        sb.AppendLine("  await browser.close();");
        sb.AppendLine("})().catch(e => { console.error(e.message); process.exit(1); });");
        return sb.ToString();
    }

    // ─── ACA Data Plane Client ────────────────────────────────────────────────────

    private string GetSandboxBasePath(string sessionId)
    {
        return $"/subscriptions/{ACA_SUBSCRIPTION_ID}/resourceGroups/{ACA_RESOURCE_GROUP}/sandboxGroups/{ACA_SANDBOX_GROUP}/sandboxes/{sessionId}";
    }

    private string GetSandboxGroupPath()
    {
        return $"/subscriptions/{ACA_SUBSCRIPTION_ID}/resourceGroups/{ACA_RESOURCE_GROUP}/sandboxGroups/{ACA_SANDBOX_GROUP}";
    }

    private async Task<string> AutoCreateSessionAsync()
    {
        var sandbox = await CreateSandboxAsync("", "2000m", "4096Mi", 10).ConfigureAwait(false);
        var sessionId = sandbox.Value<string>("id") ?? sandbox.Value<string>("name") ?? Guid.NewGuid().ToString();

        var config = new JObject
        {
            ["viewport"] = "1280x720",
            ["userAgent"] = "",
            ["locale"] = "en-US",
            ["recordHar"] = false
        };
        await WriteConfigToSandboxAsync(sessionId, config).ConfigureAwait(false);
        return sessionId;
    }

    private async Task<JObject> CreateSandboxAsync(string sessionId, string cpu, string memory, int autoSuspendMinutes)
    {
        var memoryGi = memory == "4096Mi" ? "4Gi" : memory == "8192Mi" ? "8Gi" : "4Gi";

        var requestBody = new JObject
        {
            ["sourcesRef"] = new JObject
            {
                ["diskImage"] = ACA_DISK_IS_PUBLIC
                    ? new JObject { ["name"] = ACA_DISK_IMAGE, ["isPublic"] = true }
                    : new JObject { ["id"] = ACA_DISK_IMAGE, ["isPublic"] = false }
            },
            ["vmmType"] = "CloudHypervisor",
            ["resources"] = new JObject
            {
                ["cpu"] = cpu,
                ["memory"] = memoryGi,
                ["disk"] = "30Gi"
            },
            ["lifecycle"] = new JObject
            {
                ["autoSuspendPolicy"] = new JObject
                {
                    ["enabled"] = true,
                    ["interval"] = autoSuspendMinutes * 60,
                    ["mode"] = "Memory"
                },
                ["autoDeletePolicy"] = new JObject
                {
                    ["enabled"] = false,
                    ["deleteIntervalInSeconds"] = 3600
                }
            }
        };

        var url = $"{ACA_DATA_PLANE_BASE}{GetSandboxGroupPath()}/sandboxes?includeDebug=true";
        return await SendAcaRequestAsync(HttpMethod.Put, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> RunPlaywrightInSandboxAsync(string sessionId, string script, int timeoutSeconds)
    {
        // Write script to sandbox, then execute it with NODE_PATH set so global modules resolve
        var scriptBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
        var writeCmd = $"echo '{scriptBase64}' | base64 -d > /tmp/_pw_task.js && NODE_PATH=/usr/lib/node_modules node /tmp/_pw_task.js";

        var requestBody = new JObject { ["command"] = writeCmd };
        var url = $"{ACA_DATA_PLANE_BASE}{GetSandboxBasePath(sessionId)}/executeShellCommand";
        return await SendAcaRequestAsync(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task WriteConfigToSandboxAsync(string sessionId, JObject config)
    {
        var configJson = config.ToString(Newtonsoft.Json.Formatting.None);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));
        var cmd = $"mkdir -p /workspace && echo '{base64}' | base64 -d > {CONFIG_FILE}";

        var requestBody = new JObject { ["command"] = cmd };
        var url = $"{ACA_DATA_PLANE_BASE}{GetSandboxBasePath(sessionId)}/executeShellCommand";
        await SendAcaRequestAsync(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ReadFileFromSandboxAsync(string sessionId, string filePath)
    {
        var url = $"{ACA_DATA_PLANE_BASE}{GetSandboxBasePath(sessionId)}/files{filePath}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = authHeader;
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = filePath.Split('/').Last();

        return new JObject
        {
            ["fileName"] = fileName,
            ["content"] = Convert.ToBase64String(bytes),
            ["sizeBytes"] = bytes.Length,
            ["mimeType"] = contentType
        };
    }

    private async Task DeleteSandboxAsync(string sessionId)
    {
        var url = $"{ACA_DATA_PLANE_BASE}{GetSandboxBasePath(sessionId)}";
        await SendAcaRequestAsync(HttpMethod.Delete, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> SendAcaRequestAsync(HttpMethod method, string url, JObject body)
    {
        var request = new HttpRequestMessage(method, url);

        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = authHeader;
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"ACA API error ({(int)response.StatusCode}): {responseBody}");
        }

        if (string.IsNullOrWhiteSpace(responseBody))
            return new JObject();

        return JObject.Parse(responseBody);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private JObject ParseScriptOutput(JObject execResult)
    {
        var stdout = execResult.Value<string>("stdout") ?? "";
        var stderr = execResult.Value<string>("stderr") ?? "";
        var exitCode = execResult.Value<int?>("exitCode") ?? -1;

        if (exitCode != 0)
        {
            return new JObject
            {
                ["error"] = stderr.Length > 0 ? stderr : "Script execution failed",
                ["exitCode"] = exitCode
            };
        }

        // Try to parse stdout as JSON
        stdout = stdout.Trim();
        if (stdout.StartsWith("{") || stdout.StartsWith("["))
        {
            try { return JObject.Parse(stdout); }
            catch { /* fall through */ }
        }

        return new JObject { ["output"] = stdout };
    }

    private string JsonString(string value)
    {
        return JsonConvert.SerializeObject(value ?? "");
    }

    private async Task<JObject> ReadBodyAsync()
    {
        var content = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content) ? new JObject() : JObject.Parse(content);
    }

    private HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, JObject body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ─── MCP Response Helpers ─────────────────────────────────────────────────────

    private HttpResponseMessage McpSuccess(JToken id, JObject result)
    {
        var json = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage McpError(JToken id, int code, string message, string data = null)
    {
        var err = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) err["data"] = data;
        var json = new JObject { ["jsonrpc"] = "2.0", ["error"] = err, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ─── App Insights ─────────────────────────────────────────────────────────────

    private void LogToAppInsights(string eventName, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

        try
        {
            var telemetryData = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = $"ACAPlaywright.{eventName}",
                        ["properties"] = properties != null
                            ? JObject.FromObject(properties)
                            : new JObject()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT);
            request.Content = new StringContent(
                telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );

            this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
