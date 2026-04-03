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
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    private const string ServerName = "azure-content-safety";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";
    private const string ApiVersion = "2024-09-01";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        try
        {
            _ = LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = this.Context.OperationId });

            switch (this.Context.OperationId)
            {
                case "InvokeMCP":        return await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                case "AnalyzeText":      return await HandlePassthroughAsync().ConfigureAwait(false);
                case "AnalyzeImage":     return await HandlePassthroughAsync().ConfigureAwait(false);
                case "CheckTextSafety":  return await HandleCheckTextSafetyAsync(correlationId).ConfigureAwait(false);
                case "CheckImageSafety": return await HandleCheckImageSafetyAsync(correlationId).ConfigureAwait(false);
                case "ShieldPrompt":     return await HandlePassthroughAsync().ConfigureAwait(false);
                case "DetectProtectedMaterial": return await HandlePassthroughAsync().ConfigureAwait(false);
                case "DetectProtectedCode": return await HandlePassthroughAsync().ConfigureAwait(false);
                case "DetectGroundedness": return await HandlePassthroughAsync().ConfigureAwait(false);
                case "DetectTaskAdherence": return await HandlePassthroughAsync().ConfigureAwait(false);
                case "AnalyzeCustomCategory": return await HandlePassthroughAsync().ConfigureAwait(false);
                case "ListBlocklists":   return await HandlePassthroughAsync().ConfigureAwait(false);
                case "CreateBlocklist":  return await HandlePassthroughAsync().ConfigureAwait(false);
                case "AddBlocklistItems": return await HandlePassthroughAsync().ConfigureAwait(false);
                case "ListBlocklistItems": return await HandlePassthroughAsync().ConfigureAwait(false);
                case "RemoveBlocklistItems": return await HandlePassthroughAsync().ConfigureAwait(false);
                case "DeleteBlocklist":  return await HandlePassthroughAsync().ConfigureAwait(false);
                default:                 return await HandlePassthroughAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("RequestError", new { CorrelationId = correlationId, ErrorMessage = ex.Message });
            throw;
        }
    }

    // ========================================
    // CHECK TEXT SAFETY (simplified)
    // ========================================

    private async Task<HttpResponseMessage> HandleCheckTextSafetyAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        var text = body.Value<string>("text");
        var threshold = body["threshold"]?.Value<int>() ?? 2;
        var blocklistNames = body["blocklistNames"] as JArray;

        if (string.IsNullOrWhiteSpace(text))
            return CreateErrorResponse(HttpStatusCode.BadRequest, "text is required");

        // Build the analyze request
        var analyzeRequest = new JObject { ["text"] = text };
        if (blocklistNames != null && blocklistNames.Count > 0)
            analyzeRequest["blocklistNames"] = blocklistNames;

        // Call the Content Safety API
        var uri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/contentsafety/text:analyze",
            Query = $"api-version={ApiVersion}"
        }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(
            analyzeRequest.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return response;

        var result = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

        // Process results
        var categories = result["categoriesAnalysis"] as JArray ?? new JArray();
        var blocklistMatches = result["blocklistsMatch"] as JArray ?? new JArray();

        string highestCategory = "None";
        int highestSeverity = 0;
        var catDict = new JObject();

        foreach (var cat in categories)
        {
            var name = cat.Value<string>("category");
            var severity = cat.Value<int>("severity");
            catDict[name] = severity;
            if (severity > highestSeverity)
            {
                highestSeverity = severity;
                highestCategory = name;
            }
        }

        var isSafe = highestSeverity < threshold && blocklistMatches.Count == 0;

        var formatted = new JObject
        {
            ["is_safe"] = isSafe,
            ["highest_category"] = highestCategory,
            ["highest_severity"] = highestSeverity,
            ["threshold"] = threshold,
            ["blocklist_hit"] = blocklistMatches.Count > 0,
            ["categories"] = catDict
        };

        _ = LogToAppInsights("CheckTextSafety", new
        {
            CorrelationId = correlationId,
            IsSafe = isSafe,
            HighestCategory = highestCategory,
            HighestSeverity = highestSeverity,
            Threshold = threshold
        });

        response.Content = CreateJsonContent(formatted.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    // ========================================
    // CHECK IMAGE SAFETY (simplified)
    // ========================================

    private async Task<HttpResponseMessage> HandleCheckImageSafetyAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        var imageContent = body.Value<string>("image_content");
        var imageUrl = body.Value<string>("image_url");
        var threshold = body["threshold"]?.Value<int>() ?? 2;

        if (string.IsNullOrWhiteSpace(imageContent) && string.IsNullOrWhiteSpace(imageUrl))
            return CreateErrorResponse(HttpStatusCode.BadRequest, "Either image_content (base64) or image_url is required");

        // Build the image object
        var imageObj = new JObject();
        if (!string.IsNullOrWhiteSpace(imageContent))
            imageObj["content"] = imageContent;
        else
            imageObj["blobUrl"] = imageUrl;

        var analyzeRequest = new JObject { ["image"] = imageObj };

        // Call the Content Safety API
        var uri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/contentsafety/image:analyze",
            Query = $"api-version={ApiVersion}"
        }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(
            analyzeRequest.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return response;

        var result = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        var categories = result["categoriesAnalysis"] as JArray ?? new JArray();

        string highestCategory = "None";
        int highestSeverity = 0;
        var catDict = new JObject();

        foreach (var cat in categories)
        {
            var name = cat.Value<string>("category");
            var severity = cat.Value<int>("severity");
            catDict[name] = severity;
            if (severity > highestSeverity)
            {
                highestSeverity = severity;
                highestCategory = name;
            }
        }

        var isSafe = highestSeverity < threshold;

        var formatted = new JObject
        {
            ["is_safe"] = isSafe,
            ["highest_category"] = highestCategory,
            ["highest_severity"] = highestSeverity,
            ["threshold"] = threshold,
            ["categories"] = catDict
        };

        response.Content = CreateJsonContent(formatted.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    // ========================================
    // PASSTHROUGH
    // ========================================

    private async Task<HttpResponseMessage> HandlePassthroughAsync()
    {
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
    {
        JToken requestId = null;
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");

            JObject request;
            try { request = JObject.Parse(body); }
            catch (JsonException) { return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON"); }

            var method = request.Value<string>("method") ?? "";
            requestId = request["id"];

            switch (method)
            {
                case "initialize": return HandleMcpInitialize(request, requestId);
                case "initialized": case "notifications/initialized": case "notifications/cancelled": case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());
                case "tools/list": return HandleMcpToolsList(requestId);
                case "tools/call": return await HandleMcpToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);
                case "resources/list": return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });
                case "resources/templates/list": return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });
                case "prompts/list": return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });
                case "completion/complete": return CreateJsonRpcSuccessResponse(requestId, new JObject { ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } });
                case "logging/setLevel": return CreateJsonRpcSuccessResponse(requestId, new JObject());
                default: return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex) { return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message); }
    }

    private HttpResponseMessage HandleMcpInitialize(JObject request, JToken requestId)
    {
        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["protocolVersion"] = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false },
                ["logging"] = new JObject(),
                ["completions"] = new JObject()
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = "Azure AI Content Safety",
                ["description"] = "Analyze text and images for harmful content. Detects Hate, SelfHarm, Sexual, and Violence with configurable thresholds."
            }
        });
    }

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            McpTool("check_text_safety", "Check if text is safe. Returns is_safe (true/false), highest harm category, and severity scores. Use before publishing user content or returning LLM output.",
                new JObject
                {
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "The text to check (max 10,000 characters)" },
                    ["threshold"] = new JObject { ["type"] = "integer", ["description"] = "Max allowed severity (0-6, default 2). Content at or above this is flagged unsafe." }
                }, new[] { "text" }),
            McpTool("check_image_safety", "Check if an image is safe. Returns is_safe (true/false) and severity scores per harm category.",
                new JObject
                {
                    ["image_content"] = new JObject { ["type"] = "string", ["description"] = "Base64-encoded image content" },
                    ["image_url"] = new JObject { ["type"] = "string", ["description"] = "Azure Blob Storage URL" },
                    ["threshold"] = new JObject { ["type"] = "integer", ["description"] = "Max allowed severity (0-6, default 2)" }
                }, new string[0]),
            McpTool("analyze_text", "Full text analysis with detailed severity scores for all harm categories and optional blocklist checking.",
                new JObject
                {
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "The text to analyze" },
                    ["blocklistNames"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Optional blocklist names to check" }
                }, new[] { "text" }),
            McpTool("analyze_image", "Full image analysis with detailed severity scores for all harm categories.",
                new JObject
                {
                    ["image_content"] = new JObject { ["type"] = "string", ["description"] = "Base64-encoded image" },
                    ["image_url"] = new JObject { ["type"] = "string", ["description"] = "Azure Blob Storage URL" }
                }, new string[0]),
            McpTool("shield_prompt", "Detect prompt injection attacks (jailbreak) in user prompts and indirect attacks in documents. Use before passing user input to an LLM.",
                new JObject
                {
                    ["user_prompt"] = new JObject { ["type"] = "string", ["description"] = "The user prompt to check for injection attacks" },
                    ["documents"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Documents to check for indirect injection attacks" }
                }, new string[0]),
            McpTool("detect_protected_material", "Check if AI-generated text contains known protected material (song lyrics, articles, recipes). Use on LLM output before returning to users.",
                new JObject
                {
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "The AI-generated text to check (min 110 characters)" }
                }, new[] { "text" }),
            McpTool("detect_protected_code", "Check if AI-generated code matches known code from public GitHub repos. Preview — index current through April 2023.",
                new JObject
                {
                    ["code"] = new JObject { ["type"] = "string", ["description"] = "The AI-generated code to check" }
                }, new[] { "code" }),
            McpTool("detect_groundedness", "Check if an LLM response is grounded in provided source materials. Detects hallucinations. Use after RAG retrieval to validate responses.",
                new JObject
                {
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "The LLM response to check" },
                    ["grounding_sources"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Source documents the response should be grounded in" },
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "The user's original question (for QnA tasks)" }
                }, new[] { "text", "grounding_sources" }),
            McpTool("detect_task_adherence", "Check if an AI agent's tool calls are aligned with the user's intent. Detects misaligned or premature tool invocations.",
                new JObject
                {
                    ["tools_json"] = new JObject { ["type"] = "string", ["description"] = "JSON string of available tools array" },
                    ["messages_json"] = new JObject { ["type"] = "string", ["description"] = "JSON string of conversation messages array" }
                }, new[] { "tools_json", "messages_json" }),
            McpTool("analyze_custom_category", "Check text against a custom category defined with a name, definition, and optional examples. No training needed.",
                new JObject
                {
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "Text to analyze" },
                    ["category_name"] = new JObject { ["type"] = "string", ["description"] = "Custom category name (e.g., 'PoliticalContent')" },
                    ["definition"] = new JObject { ["type"] = "string", ["description"] = "Definition of what this category represents" }
                }, new[] { "text", "category_name", "definition" })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private static JObject McpTool(string name, string description, JObject properties, string[] required)
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

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");

        try
        {
            JObject toolResult;
            switch (toolName.ToLowerInvariant())
            {
                case "check_text_safety":
                    toolResult = await ExecuteCheckTextAsync(arguments).ConfigureAwait(false);
                    break;
                case "check_image_safety":
                    toolResult = await ExecuteCheckImageAsync(arguments).ConfigureAwait(false);
                    break;
                case "analyze_text":
                    toolResult = await ExecuteAnalyzeTextAsync(arguments).ConfigureAwait(false);
                    break;
                case "analyze_image":
                    toolResult = await ExecuteAnalyzeImageAsync(arguments).ConfigureAwait(false);
                    break;
                case "shield_prompt":
                    toolResult = await ExecuteShieldPromptAsync(arguments).ConfigureAwait(false);
                    break;
                case "detect_protected_material":
                    toolResult = await ExecuteDetectProtectedMaterialAsync(arguments).ConfigureAwait(false);
                    break;
                case "detect_protected_code":
                    toolResult = await ExecuteDetectProtectedCodeAsync(arguments).ConfigureAwait(false);
                    break;
                case "detect_groundedness":
                    toolResult = await ExecuteDetectGroundednessAsync(arguments).ConfigureAwait(false);
                    break;
                case "detect_task_adherence":
                    toolResult = await ExecuteDetectTaskAdherenceAsync(arguments).ConfigureAwait(false);
                    break;
                case "analyze_custom_category":
                    toolResult = await ExecuteAnalyzeCustomCategoryAsync(arguments).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentException($"Unknown tool: {toolName}");
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented) } },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // MCP TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<JObject> ExecuteAnalyzeTextAsync(JObject args)
    {
        var text = args.Value<string>("text") ?? throw new ArgumentException("'text' is required");
        var req = new JObject { ["text"] = text };
        var blocklists = args["blocklistNames"] as JArray;
        if (blocklists != null && blocklists.Count > 0) req["blocklistNames"] = blocklists;

        return await SendSafetyRequestAsync("/contentsafety/text:analyze", req).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAnalyzeImageAsync(JObject args)
    {
        var imageObj = new JObject();
        var content = args.Value<string>("image_content");
        var url = args.Value<string>("image_url");
        if (!string.IsNullOrWhiteSpace(content)) imageObj["content"] = content;
        else if (!string.IsNullOrWhiteSpace(url)) imageObj["blobUrl"] = url;
        else throw new ArgumentException("Either 'image_content' or 'image_url' is required");

        return await SendSafetyRequestAsync("/contentsafety/image:analyze", new JObject { ["image"] = imageObj }).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCheckTextAsync(JObject args)
    {
        var text = args.Value<string>("text") ?? throw new ArgumentException("'text' is required");
        var threshold = args["threshold"]?.Value<int>() ?? 2;

        var req = new JObject { ["text"] = text };
        var result = await SendSafetyRequestAsync("/contentsafety/text:analyze", req).ConfigureAwait(false);

        return BuildSafetyResult(result["categoriesAnalysis"] as JArray, result["blocklistsMatch"] as JArray, threshold);
    }

    private async Task<JObject> ExecuteCheckImageAsync(JObject args)
    {
        var imageObj = new JObject();
        var content = args.Value<string>("image_content");
        var url = args.Value<string>("image_url");
        if (!string.IsNullOrWhiteSpace(content)) imageObj["content"] = content;
        else if (!string.IsNullOrWhiteSpace(url)) imageObj["blobUrl"] = url;
        else throw new ArgumentException("Either 'image_content' or 'image_url' is required");

        var threshold = args["threshold"]?.Value<int>() ?? 2;
        var result = await SendSafetyRequestAsync("/contentsafety/image:analyze", new JObject { ["image"] = imageObj }).ConfigureAwait(false);

        return BuildSafetyResult(result["categoriesAnalysis"] as JArray, null, threshold);
    }

    private static JObject BuildSafetyResult(JArray categories, JArray blocklistMatches, int threshold)
    {
        categories = categories ?? new JArray();
        string highestCategory = "None";
        int highestSeverity = 0;
        var catDict = new JObject();

        foreach (var cat in categories)
        {
            var name = cat.Value<string>("category");
            var severity = cat.Value<int>("severity");
            catDict[name] = severity;
            if (severity > highestSeverity) { highestSeverity = severity; highestCategory = name; }
        }

        var blocklistHit = blocklistMatches != null && blocklistMatches.Count > 0;
        return new JObject
        {
            ["is_safe"] = highestSeverity < threshold && !blocklistHit,
            ["highest_category"] = highestCategory,
            ["highest_severity"] = highestSeverity,
            ["threshold"] = threshold,
            ["blocklist_hit"] = blocklistHit,
            ["categories"] = catDict
        };
    }

    private async Task<JObject> ExecuteShieldPromptAsync(JObject args)
    {
        var req = new JObject();
        var userPrompt = args.Value<string>("user_prompt");
        var documents = args["documents"] as JArray;
        if (!string.IsNullOrWhiteSpace(userPrompt)) req["userPrompt"] = userPrompt;
        if (documents != null && documents.Count > 0) req["documents"] = documents;
        if (string.IsNullOrWhiteSpace(userPrompt) && (documents == null || documents.Count == 0))
            throw new ArgumentException("Either 'user_prompt' or 'documents' is required");

        return await SendSafetyRequestAsync("/contentsafety/text:shieldPrompt", req).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDetectProtectedMaterialAsync(JObject args)
    {
        var text = args.Value<string>("text") ?? throw new ArgumentException("'text' is required (min 110 characters)");
        return await SendSafetyRequestAsync("/contentsafety/text:detectProtectedMaterial", new JObject { ["text"] = text }).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDetectProtectedCodeAsync(JObject args)
    {
        var code = args.Value<string>("code") ?? throw new ArgumentException("'code' is required");
        return await SendSafetyRequestAsync("/contentsafety/text:detectProtectedMaterialForCode", new JObject { ["code"] = code }, "2024-09-15-preview").ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDetectGroundednessAsync(JObject args)
    {
        var text = args.Value<string>("text") ?? throw new ArgumentException("'text' is required");
        var sources = args["grounding_sources"] as JArray ?? throw new ArgumentException("'grounding_sources' is required");
        var query = args.Value<string>("query");

        var req = new JObject
        {
            ["domain"] = "Generic",
            ["task"] = string.IsNullOrWhiteSpace(query) ? "Summarization" : "QnA",
            ["text"] = text,
            ["groundingSources"] = sources,
            ["reasoning"] = false
        };
        if (!string.IsNullOrWhiteSpace(query))
            req["qna"] = new JObject { ["query"] = query };

        return await SendSafetyRequestAsync("/contentsafety/text:detectGroundedness", req, "2024-09-15-preview").ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDetectTaskAdherenceAsync(JObject args)
    {
        var toolsJson = args.Value<string>("tools_json") ?? throw new ArgumentException("'tools_json' is required");
        var messagesJson = args.Value<string>("messages_json") ?? throw new ArgumentException("'messages_json' is required");

        var req = new JObject
        {
            ["tools"] = JArray.Parse(toolsJson),
            ["messages"] = JArray.Parse(messagesJson)
        };

        return await SendSafetyRequestAsync("/contentsafety/agent:analyzeTaskAdherence", req, "2025-09-15-preview").ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAnalyzeCustomCategoryAsync(JObject args)
    {
        var text = args.Value<string>("text") ?? throw new ArgumentException("'text' is required");
        var categoryName = args.Value<string>("category_name") ?? throw new ArgumentException("'category_name' is required");
        var definition = args.Value<string>("definition") ?? throw new ArgumentException("'definition' is required");

        var req = new JObject
        {
            ["text"] = text,
            ["categoryName"] = categoryName,
            ["definition"] = definition
        };

        return await SendSafetyRequestAsync("/contentsafety/text:analyzeCustomCategory", req, "2024-09-15-preview").ConfigureAwait(false);
    }

    private async Task<JObject> SendSafetyRequestAsync(string path, JObject body)
    {
        return await SendSafetyRequestAsync(path, body, ApiVersion).ConfigureAwait(false);
    }

    private async Task<JObject> SendSafetyRequestAsync(string path, JObject body, string apiVersion)
    {
        var url = $"https://{this.Context.Request.RequestUri.Host}{path}?api-version={apiVersion}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);

        if (this.Context.Request.Headers.Contains("Ocp-Apim-Subscription-Key"))
            request.Headers.Add("Ocp-Apim-Subscription-Key", this.Context.Request.Headers.GetValues("Ocp-Apim-Subscription-Key"));

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Content Safety API failed ({(int)response.StatusCode}): {content}");
        return JObject.Parse(content);
    }

    // ========================================
    // HELPERS
    // ========================================

    private HttpResponseMessage CreateErrorResponse(HttpStatusCode status, string message)
    {
        return new HttpResponseMessage(status) { Content = CreateJsonContent($"{{\"error\": \"{message}\"}}") };
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var iKey = ExtractPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var endpoint = ExtractPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint") ?? "https://dc.services.visualstudio.com/";
            if (string.IsNullOrEmpty(iKey)) return;

            var props = new Dictionary<string, string> { ["ServerName"] = ServerName, ["ServerVersion"] = ServerVersion };
            if (properties != null)
                foreach (var p in JObject.Parse(JsonConvert.SerializeObject(properties)).Properties())
                    props[p.Name] = p.Value?.ToString() ?? "";

            var telemetry = new { name = $"Microsoft.ApplicationInsights.{iKey}.Event", time = DateTime.UtcNow.ToString("o"), iKey, data = new { baseType = "EventData", baseData = new { ver = 2, name = eventName, properties = props } } };
            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint.TrimEnd('/') + "/v2/track")) { Content = new StringContent(JsonConvert.SerializeObject(telemetry), Encoding.UTF8, "application/json") };
            await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractPart(string cs, string key)
    {
        if (string.IsNullOrEmpty(cs)) return null;
        foreach (var p in cs.Split(';'))
            if (p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return p.Substring(key.Length + 1);
        return null;
    }
}
