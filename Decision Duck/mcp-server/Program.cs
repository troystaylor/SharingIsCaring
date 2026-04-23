using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;

const int MaxMcpRequestBodyBytes = 128 * 1024;
const int MaxSummaryStringLength = 120;

var builder = WebApplication.CreateBuilder(args);

var appInsightsConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = appInsightsConnectionString;
    });
}

builder.Services.AddHttpClient("llm", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    MaxConnectionsPerServer = 50
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "decision-duck-mcp" }));

app.MapGet("/mcp", () => Results.Ok(new
{
    status = "ok",
    service = "decision-duck-mcp",
    message = "MCP endpoint is available. Use POST /mcp with a JSON-RPC 2.0 payload."
}));

app.MapPost("/mcp", async (HttpRequest request, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("DecisionDuckMcp");

    if (request.ContentLength is > MaxMcpRequestBodyBytes)
    {
        logger.LogWarning("Rejected MCP request over size limit. ContentLength={ContentLength}", request.ContentLength);
        return Results.Json(JsonRpc.Error(null, -32600, $"Request body too large. Limit is {MaxMcpRequestBodyBytes} bytes."), statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    var bodyText = await ReadRequestBodyAsync(request.Body, MaxMcpRequestBodyBytes, cancellationToken);
    if (bodyText is null)
    {
        logger.LogWarning("Rejected MCP request exceeding streaming size limit.");
        return Results.Json(JsonRpc.Error(null, -32600, $"Request body too large. Limit is {MaxMcpRequestBodyBytes} bytes."), statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    JsonObject? payload;

    try
    {
        payload = JsonNode.Parse(bodyText) as JsonObject;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Invalid JSON body.");
        return Results.Json(JsonRpc.Error(null, -32700, "Parse error"));
    }

    if (payload is null)
    {
        return Results.Json(JsonRpc.Error(null, -32700, "Parse error"));
    }

    var jsonRpcVersion = payload["jsonrpc"]?.GetValue<string>();
    if (!string.Equals(jsonRpcVersion, "2.0", StringComparison.Ordinal))
    {
        return Results.Json(JsonRpc.Error(payload["id"], -32600, "Invalid Request: jsonrpc must be '2.0'"));
    }

    var method = payload["method"]?.GetValue<string>();
    var id = payload["id"];

    if (payload["params"] is not null && payload["params"] is not JsonObject)
    {
        return Results.Json(JsonRpc.Error(id, -32600, "Invalid Request: params must be an object"));
    }

    var @params = payload["params"] as JsonObject ?? new JsonObject();

    if (string.IsNullOrWhiteSpace(method))
    {
        return Results.Json(JsonRpc.Error(id, -32600, "Invalid Request"));
    }

    var validationError = ValidateMethodRequest(method, @params);
    if (validationError is not null)
    {
        return Results.Json(JsonRpc.Error(id, -32602, validationError));
    }

    logger.LogInformation(
        "MCP request received: Method={Method} Id={RequestId} Params={ParamsSummary}",
        method,
        SummarizeJsonNode(id),
        SummarizeParams(@params)
    );

    try
    {
        switch (method)
        {
            case "initialize":
                logger.LogInformation("MCP initialize requested.");
                return Results.Json(JsonRpc.Result(id, new JsonObject
                {
                    ["protocolVersion"] = "2025-03-26",
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject(),
                        ["resources"] = new JsonObject()
                    },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "decision-duck-mcp",
                        ["version"] = "1.0.0"
                    }
                }));

            case "tools/list":
                logger.LogInformation("MCP tools/list requested.");
                return Results.Json(JsonRpc.Result(id, new JsonObject
                {
                    ["tools"] = ToolCatalog.ListTools()
                }));

            case "tools/call":
                return await HandleToolCallAsync(id, @params, httpClientFactory, logger, cancellationToken);

            case "resources/list":
                logger.LogInformation("MCP resources/list requested.");
                return Results.Json(JsonRpc.Result(id, new JsonObject
                {
                    ["resources"] = ResourceCatalog.ListResources()
                }));

            case "resources/read":
                logger.LogInformation("MCP resources/read requested: Params={ParamsSummary}", SummarizeParams(@params));
                return HandleResourceRead(id, @params);

            default:
                return Results.Json(JsonRpc.Error(id, -32601, "Method not found"));
        }
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Bad request for method {Method}", method);
        return Results.Json(JsonRpc.Error(id, -32602, ex.Message));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled server error for method {Method}", method);
        return Results.Json(JsonRpc.Error(id, -32603, "Internal error"));
    }
});

app.Run();

static async Task<IResult> HandleToolCallAsync(JsonNode? id, JsonObject @params, IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken)
{
    var name = @params["name"]?.GetValue<string>();
    var args = @params["arguments"] as JsonObject ?? new JsonObject();

    if (string.IsNullOrWhiteSpace(name))
    {
        throw new ArgumentException("params.name is required");
    }

    var toolValidationError = ValidateToolArguments(name, args);
    if (toolValidationError is not null)
    {
        throw new ArgumentException(toolValidationError);
    }

    logger.LogInformation("MCP tool call started: Tool={ToolName} Arguments={ArgumentSummary}", name, SummarizeParams(args));

    string resultText;
    JsonObject? llmMetadata = null;

    try
    {
        switch (name)
        {
            case "get_second_opinion":
            {
                var modelResult = await CallSecondOpinionAsync(args, httpClientFactory, logger, cancellationToken);
                resultText = modelResult.Text;
                llmMetadata = BuildLlmMetadata(modelResult);
                break;
            }
            case "analyze_risk":
            {
                var modelResult = await CallRiskAnalysisAsync(args, httpClientFactory, logger, cancellationToken);
                resultText = modelResult.Text;
                llmMetadata = BuildLlmMetadata(modelResult);
                break;
            }
            case "identify_cognitive_biases":
            {
                var modelResult = await CallBiasAnalysisAsync(args, httpClientFactory, logger, cancellationToken);
                resultText = modelResult.Text;
                llmMetadata = BuildLlmMetadata(modelResult);
                break;
            }
            case "comparative_analysis":
            {
                var modelResult = await CallComparativeAnalysisAsync(args, httpClientFactory, logger, cancellationToken);
                resultText = modelResult.Text;
                llmMetadata = BuildLlmMetadata(modelResult);
                break;
            }
            case "list_frameworks":
                resultText = ResourceCatalog.ListFrameworkNames();
                break;
            case "get_framework":
                resultText = ResourceCatalog.GetFrameworkById(args["framework_id"]?.GetValue<string>() ?? throw new ArgumentException("framework_id is required"));
                break;
            default:
                throw new ArgumentException($"Unknown tool: {name}");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Tool call failed: {ToolName}", name);
        return Results.Json(JsonRpc.Error(id, -32603, $"Tool execution error: {ex.Message}"));
    }

    logger.LogInformation("Tool call success: {ToolName}", name);

    var visibleResultText = resultText;
    if (llmMetadata?[
            "model"] is JsonValue modelValue &&
        modelValue.TryGetValue<string>(out var modelName) &&
        !string.IsNullOrWhiteSpace(modelName))
    {
        visibleResultText += $"\n\nModel used: {modelName}";
    }

    if (name == "comparative_analysis")
    {
        var structuredContent = new JsonObject
        {
            ["context"] = args["context"]?.DeepClone(),
            ["options"] = args["options"]?.DeepClone(),
            ["criteria"] = args["criteria"]?.DeepClone(),
            ["analysis"] = resultText
        };

        if (llmMetadata is not null)
        {
            structuredContent["llm"] = llmMetadata;
        }

        return Results.Json(JsonRpc.Result(id, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = visibleResultText
                }
            },
            ["structuredContent"] = structuredContent
        }));
    }

    var result = new JsonObject
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = visibleResultText
            }
        }
    };

    if (llmMetadata is not null)
    {
        result["structuredContent"] = new JsonObject
        {
            ["llm"] = llmMetadata
        };
    }

    return Results.Json(JsonRpc.Result(id, result));
}

static IResult HandleResourceRead(JsonNode? id, JsonObject @params)
{
    var uri = @params["uri"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(uri))
    {
        throw new ArgumentException("params.uri is required");
    }

    var content = ResourceCatalog.ReadResource(uri);
    var mimeType = ResourceCatalog.GetMimeType(uri);

    return Results.Json(JsonRpc.Result(id, new JsonObject
    {
        ["contents"] = new JsonArray
        {
            new JsonObject
            {
                ["uri"] = uri,
                ["mimeType"] = mimeType,
                ["text"] = content
            }
        }
    }));
}

static async Task<ModelCallResult> CallSecondOpinionAsync(JsonObject args, IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken)
{
    var prompt = args["prompt"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(prompt))
    {
        throw new ArgumentException("prompt is required");
    }

    var depth = args["analysis_depth"]?.GetValue<string>() ?? "balanced";
    var focus = args["focus_area"]?.GetValue<string>();

    var systemPrompt = "You are Decision Duck. Provide a second opinion with assumptions, risks, tradeoffs, and recommended next steps. " +
                       (depth == "quick" ? "Be concise." : depth == "deep" ? "Provide deep analysis." : "Provide balanced detail.") +
                       (string.IsNullOrWhiteSpace(focus) ? string.Empty : $" Focus on: {focus}.");

    return await CallModelAsync("get_second_opinion", systemPrompt, prompt, httpClientFactory, logger, cancellationToken);
}

static async Task<ModelCallResult> CallRiskAnalysisAsync(JsonObject args, IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken)
{
    var scenario = args["scenario"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(scenario))
    {
        throw new ArgumentException("scenario is required");
    }

    var context = args["context"]?.GetValue<string>();
    var categories = args["risk_categories"] as JsonArray;

    var userPrompt = new StringBuilder()
        .AppendLine("Analyze risk for this scenario:")
        .AppendLine(scenario);

    if (!string.IsNullOrWhiteSpace(context))
    {
        userPrompt.AppendLine().AppendLine($"Context: {context}");
    }

    if (categories is { Count: > 0 })
    {
        userPrompt.AppendLine().AppendLine("Prioritized categories: " + string.Join(", ", categories.Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    var systemPrompt = "You are a risk analyst. Return key risks, probability/impact, mitigations, and leading indicators.";
    try
    {
        return await CallModelAsync("analyze_risk", systemPrompt, userPrompt.ToString(), httpClientFactory, logger, cancellationToken);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("429 Too Many Requests", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning("Falling back to local risk analysis due to model throttling.");
        return new ModelCallResult(BuildRiskFallback(scenario, context, categories), "fallback", new JsonArray());
    }
}

static async Task<ModelCallResult> CallBiasAnalysisAsync(JsonObject args, IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken)
{
    var analysis = args["analysis"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(analysis))
    {
        throw new ArgumentException("analysis is required");
    }

    var focusBiases = args["focus_biases"] as JsonArray;
    var userPrompt = new StringBuilder()
        .AppendLine("Check this reasoning for cognitive bias:")
        .AppendLine(analysis);

    if (focusBiases is { Count: > 0 })
    {
        userPrompt.AppendLine().AppendLine("Focus biases: " + string.Join(", ", focusBiases.Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    var systemPrompt = "You are a cognitive bias reviewer. Identify likely biases, why they matter, and practical corrections.";
    try
    {
        return await CallModelAsync("identify_cognitive_biases", systemPrompt, userPrompt.ToString(), httpClientFactory, logger, cancellationToken);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("429 Too Many Requests", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning("Falling back to local bias analysis due to model throttling.");
        return new ModelCallResult(BuildBiasFallback(analysis, focusBiases), "fallback", new JsonArray());
    }
}

    static async Task<ModelCallResult> CallComparativeAnalysisAsync(JsonObject args, IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken)
{
    var context = args["context"]?.GetValue<string>();
    var options = args["options"] as JsonArray;

    if (string.IsNullOrWhiteSpace(context))
    {
        throw new ArgumentException("context is required");
    }

    if (options is not { Count: > 0 })
    {
        throw new ArgumentException("options is required");
    }

    var criteria = args["criteria"] as JsonArray;

    var userPrompt = new StringBuilder()
        .AppendLine($"Context: {context}")
        .AppendLine("Options:")
        .AppendLine(string.Join(Environment.NewLine, options.Select(x => "- " + x?.GetValue<string>())));

    if (criteria is { Count: > 0 })
    {
        userPrompt.AppendLine().AppendLine("Criteria: " + string.Join(", ", criteria.Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    var systemPrompt = "You are a strategy analyst. Compare options side-by-side, call out tradeoffs, and recommend one with rationale.";
    try
    {
        return await CallModelAsync("comparative_analysis", systemPrompt, userPrompt.ToString(), httpClientFactory, logger, cancellationToken);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("429 Too Many Requests", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning("Falling back to local comparative analysis due to model throttling.");
        return new ModelCallResult(BuildComparativeFallback(context, options, criteria), "fallback", new JsonArray());
    }
}

    static async Task<ModelCallResult> CallModelAsync(string operationName, string systemPrompt, string userPrompt, IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken)
{
    var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT") ?? "http://localhost:60311/v1";
    var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "phi-4";
    var apiKey = Environment.GetEnvironmentVariable("FOUNDRY_API_KEY");
    var isAzureOpenAi = endpoint.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase) ||
                        endpoint.Contains(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase);

    var normalizedBase = endpoint.TrimEnd('/');
    if (isAzureOpenAi && !normalizedBase.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
    {
        normalizedBase += "/openai/v1";
    }

    var client = httpClientFactory.CreateClient("llm");

    logger.LogInformation(
        "Foundry call prepared: Operation={Operation} Endpoint={Endpoint} Model={Model} AzureOpenAI={IsAzureOpenAi}",
        operationName,
        endpoint,
        model,
        isAzureOpenAi
    );

    for (var attempt = 1; attempt <= 3; attempt++)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            temperature = 0.4,
            top_p = 0.95,
            max_tokens = 900,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, normalizedBase + "/chat/completions");
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (isAzureOpenAi)
            {
                request.Headers.Add("api-key", apiKey);
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }
        else if (isAzureOpenAi)
        {
            var token = await RuntimeState.SharedDefaultAzureCredential.GetTokenAsync(
                new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
                cancellationToken
            );

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        try
        {
            logger.LogInformation("Foundry call attempt started: Operation={Operation} Attempt={Attempt}", operationName, attempt);
            using var response = await client.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Model call failed for {Operation} on attempt {Attempt}: {StatusCode} {ReasonPhrase} {Snippet}",
                    operationName,
                    attempt,
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    Truncate(raw, 300)
                );

                if (attempt < 3 && IsTransient(response.StatusCode))
                {
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                    continue;
                }

                throw new InvalidOperationException($"Model call failed: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(raw, 300)}");
            }

            var json = JsonNode.Parse(raw)?.AsObject();
            var text = ExtractModelText(json);

            if (!string.IsNullOrWhiteSpace(text))
            {
                logger.LogInformation("Foundry call success: Operation={Operation} Attempt={Attempt} ResponseLength={ResponseLength}", operationName, attempt, text.Length);
                var responseModel = json?["model"]?.GetValue<string>() ?? model;
                var sourceAnnotations = ExtractSourceAnnotations(json);
                return new ModelCallResult(text, responseModel, sourceAnnotations);
            }

            logger.LogWarning("Model returned no text for {Operation} on attempt {Attempt}. Raw: {Snippet}", operationName, attempt, Truncate(raw, 300));

            if (attempt < 3)
            {
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"No model response text returned. Raw: {Truncate(raw, 300)}");
        }
        catch (Exception ex) when (attempt < 3 && (ex is HttpRequestException || ex is TaskCanceledException))
        {
            logger.LogWarning(ex, "Transient model call error for {Operation} on attempt {Attempt}", operationName, attempt);
            await Task.Delay(GetRetryDelay(attempt), cancellationToken);
        }
    }

    throw new InvalidOperationException($"Model call failed after retries for {operationName}.");
}

static string SummarizeParams(JsonObject? value)
{
    if (value is null || value.Count == 0)
    {
        return "{}";
    }

    var summary = new JsonObject();

    foreach (var pair in value)
    {
        summary[pair.Key] = pair.Value switch
        {
            null => null,
            JsonValue jsonValue => SummarizeScalar(jsonValue),
            JsonArray jsonArray => $"array({jsonArray.Count})",
            JsonObject jsonObject => $"object({jsonObject.Count})",
            _ => "node"
        };
    }

    return summary.ToJsonString();
}

static string SummarizeJsonNode(JsonNode? value) => value?.ToJsonString() ?? "null";

static string SummarizeScalar(JsonValue value)
{
    if (value.TryGetValue<string>(out var stringValue))
    {
        return $"string(len={Math.Min(stringValue.Length, MaxSummaryStringLength)})";
    }

    if (value.TryGetValue<bool>(out _))
    {
        return "bool";
    }

    if (value.TryGetValue<int>(out _) || value.TryGetValue<long>(out _) || value.TryGetValue<double>(out _) || value.TryGetValue<decimal>(out _))
    {
        return "number";
    }

    return "value";
}

static TimeSpan GetRetryDelay(int attempt)
{
    var exponentialMs = Math.Min(8000, 500 * (int)Math.Pow(2, Math.Max(attempt - 1, 0)));
    var jitterMs = Random.Shared.Next(100, 700);
    return TimeSpan.FromMilliseconds(exponentialMs + jitterMs);
}

static string? ValidateMethodRequest(string method, JsonObject @params)
{
    return method switch
    {
        "initialize" when !IsStringOrMissing(@params, "protocolVersion") => "params.protocolVersion must be a string",
        "tools/call" when !IsString(@params, "name") => "params.name must be a non-empty string",
        "tools/call" when !IsObjectOrMissing(@params, "arguments") => "params.arguments must be an object when provided",
        "resources/read" when !IsString(@params, "uri") => "params.uri must be a non-empty string",
        _ => null
    };
}

static string? ValidateToolArguments(string toolName, JsonObject args)
{
    return toolName switch
    {
        "get_second_opinion" when !IsString(args, "prompt") => "prompt must be a non-empty string",
        "analyze_risk" when !IsString(args, "scenario") => "scenario must be a non-empty string",
        "analyze_risk" when !IsStringArrayOrMissing(args, "risk_categories") => "risk_categories must be an array of strings",
        "identify_cognitive_biases" when !IsString(args, "analysis") => "analysis must be a non-empty string",
        "identify_cognitive_biases" when !IsStringArrayOrMissing(args, "focus_biases") => "focus_biases must be an array of strings",
        "comparative_analysis" when !IsString(args, "context") => "context must be a non-empty string",
        "comparative_analysis" when !IsStringArray(args, "options") => "options must be a non-empty array of strings",
        "comparative_analysis" when !IsStringArrayOrMissing(args, "criteria") => "criteria must be an array of strings",
        "get_framework" when !IsString(args, "framework_id") => "framework_id must be a non-empty string",
        _ => null
    };
}

static bool IsString(JsonObject obj, string name) =>
    obj[name] is JsonValue value &&
    value.TryGetValue<string>(out var text) &&
    !string.IsNullOrWhiteSpace(text);

static bool IsStringOrMissing(JsonObject obj, string name) =>
    obj[name] is null || IsString(obj, name);

static bool IsObjectOrMissing(JsonObject obj, string name) =>
    obj[name] is null || obj[name] is JsonObject;

static bool IsStringArray(JsonObject obj, string name)
{
    if (obj[name] is not JsonArray array || array.Count == 0)
    {
        return false;
    }

    return array.All(item => item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text));
}

static bool IsStringArrayOrMissing(JsonObject obj, string name)
{
    if (obj[name] is null)
    {
        return true;
    }

    if (obj[name] is not JsonArray array)
    {
        return false;
    }

    return array.All(item => item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text));
}

static async Task<string?> ReadRequestBodyAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
{
    var buffer = new byte[8192];
    await using var memory = new MemoryStream();
    var totalRead = 0;

    while (true)
    {
        var read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        if (read == 0)
        {
            break;
        }

        totalRead += read;
        if (totalRead > maxBytes)
        {
            return null;
        }

        await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
    }

    return Encoding.UTF8.GetString(memory.ToArray());
}

static JsonObject BuildLlmMetadata(ModelCallResult modelResult) => new()
{
    ["model"] = modelResult.Model,
    ["sourceAnnotations"] = modelResult.SourceAnnotations
};

static bool IsTransient(System.Net.HttpStatusCode statusCode) =>
    statusCode == System.Net.HttpStatusCode.TooManyRequests ||
    statusCode == System.Net.HttpStatusCode.RequestTimeout ||
    statusCode == System.Net.HttpStatusCode.BadGateway ||
    statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
    statusCode == System.Net.HttpStatusCode.GatewayTimeout ||
    (int)statusCode >= 500;

static string? ExtractModelText(JsonObject? responseJson)
{
    var contentNode = responseJson?["choices"]?[0]?["message"]?["content"];

    return contentNode switch
    {
        JsonValue value => value.GetValue<string>(),
        JsonArray array => string.Join(
            "\n",
            array
                .Select(item => item?["text"]?.GetValue<string>() ?? item?["content"]?.GetValue<string>())
                .Where(text => !string.IsNullOrWhiteSpace(text))
        ),
        _ => null
    };
}

static JsonArray ExtractSourceAnnotations(JsonObject? responseJson)
{
    var annotations = new JsonArray();

    AppendAnnotations(annotations, responseJson?["choices"]?[0]?["message"]?["context"]?["citations"], "message.context.citations");
    AppendAnnotations(annotations, responseJson?["choices"]?[0]?["message"]?["annotations"], "message.annotations");
    AppendAnnotations(annotations, responseJson?["citations"], "citations");
    AppendAnnotations(annotations, responseJson?["annotations"], "annotations");

    var contentArray = responseJson?["choices"]?[0]?["message"]?["content"] as JsonArray;
    if (contentArray is not null)
    {
        foreach (var item in contentArray)
        {
            if (item is JsonObject contentObject)
            {
                AppendAnnotations(annotations, contentObject["annotations"], "message.content.annotations");
            }
        }
    }

    return annotations;
}

static void AppendAnnotations(JsonArray output, JsonNode? candidate, string source)
{
    if (candidate is not JsonArray values)
    {
        return;
    }

    foreach (var item in values)
    {
        if (item is JsonObject obj)
        {
            var clone = obj.DeepClone() as JsonObject ?? new JsonObject();
            clone["_source"] = source;
            output.Add(clone);
        }
        else if (item is not null)
        {
            output.Add(new JsonObject
            {
                ["value"] = item.DeepClone(),
                ["_source"] = source
            });
        }
    }
}

static string Truncate(string? value, int maxLength)
{
    if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
    {
        return value ?? string.Empty;
    }

    return value[..maxLength] + "...";
}

static string BuildComparativeFallback(string context, JsonArray options, JsonArray? criteria)
{
    var optionList = options
        .Select(o => o?.GetValue<string>())
        .Where(o => !string.IsNullOrWhiteSpace(o))
        .ToList();

    var criteriaList = criteria?
        .Select(c => c?.GetValue<string>())
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .ToList() ?? [];

    var sb = new StringBuilder();
    sb.AppendLine("## Comparative Analysis (Fallback Mode)");
    sb.AppendLine();
    sb.AppendLine($"Context: {context}");
    sb.AppendLine();
    sb.AppendLine("### Options");
    foreach (var opt in optionList)
    {
        sb.AppendLine($"- {opt}");
    }

    if (criteriaList.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("### Criteria");
        foreach (var c in criteriaList)
        {
            sb.AppendLine($"- {c}");
        }
    }

    sb.AppendLine();
    sb.AppendLine("### Quick Tradeoff Matrix");
    sb.AppendLine("| Option | Strengths | Risks |");
    sb.AppendLine("|---|---|---|");
    foreach (var opt in optionList)
    {
        sb.AppendLine($"| {opt} | Strong potential fit for one or more criteria. | Requires validation against implementation constraints. |");
    }

    sb.AppendLine();
    sb.AppendLine("### Recommendation");
    sb.AppendLine("Use the option with the strongest alignment to your top two criteria and run a short pilot with clear success metrics.");
    sb.AppendLine();
    sb.AppendLine("Note: This response used fallback mode because the model provider is currently rate-limiting requests.");

    return sb.ToString();
}

static string BuildRiskFallback(string scenario, string? context, JsonArray? categories)
{
    var categoryList = categories?
        .Select(c => c?.GetValue<string>())
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .ToList() ?? [];

    var sb = new StringBuilder();
    sb.AppendLine("## Risk Analysis (Fallback Mode)");
    sb.AppendLine();
    sb.AppendLine($"Scenario: {scenario}");
    if (!string.IsNullOrWhiteSpace(context))
    {
        sb.AppendLine($"Context: {context}");
    }

    if (categoryList.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("Priority categories:");
        foreach (var c in categoryList)
        {
            sb.AppendLine($"- {c}");
        }
    }

    sb.AppendLine();
    sb.AppendLine("### Initial risk register");
    sb.AppendLine("- Delivery risk: plan for phased rollout and rollback path.");
    sb.AppendLine("- Quality risk: define pre-release acceptance criteria and monitoring.");
    sb.AppendLine("- Operational risk: assign incident owners and escalation steps.");
    sb.AppendLine();
    sb.AppendLine("### Mitigations");
    sb.AppendLine("- Use canary exposure and clear stop conditions.");
    sb.AppendLine("- Add health, latency, and error-rate alerts before launch.");
    sb.AppendLine("- Run a short game day for rollback readiness.");
    sb.AppendLine();
    sb.AppendLine("Note: This response used fallback mode because the model provider is currently rate-limiting requests.");

    return sb.ToString();
}

static string BuildBiasFallback(string analysis, JsonArray? focusBiases)
{
    var focusList = focusBiases?
        .Select(b => b?.GetValue<string>())
        .Where(b => !string.IsNullOrWhiteSpace(b))
        .ToList() ?? [];

    var sb = new StringBuilder();
    sb.AppendLine("## Cognitive Bias Review (Fallback Mode)");
    sb.AppendLine();
    sb.AppendLine("Input reasoning summary:");
    sb.AppendLine(analysis);

    if (focusList.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("Requested focus biases:");
        foreach (var b in focusList)
        {
            sb.AppendLine($"- {b}");
        }
    }

    sb.AppendLine();
    sb.AppendLine("### Suggested challenge questions");
    sb.AppendLine("- What evidence would falsify this conclusion?");
    sb.AppendLine("- Are we overweighting familiar or recent examples?");
    sb.AppendLine("- What would an independent reviewer disagree with?");
    sb.AppendLine();
    sb.AppendLine("### Practical corrections");
    sb.AppendLine("- Define decision criteria before selecting an option.");
    sb.AppendLine("- Compare at least one disconfirming alternative.");
    sb.AppendLine("- Require explicit evidence quality ratings for key claims.");
    sb.AppendLine();
    sb.AppendLine("Note: This response used fallback mode because the model provider is currently rate-limiting requests.");

    return sb.ToString();
}

sealed record ModelCallResult(string Text, string Model, JsonArray SourceAnnotations);

static class RuntimeState
{
    public static readonly DefaultAzureCredential SharedDefaultAzureCredential = new();
}

static class JsonRpc
{
    public static JsonObject Result(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result
    };

    public static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        }
    };
}

static class ToolCatalog
{
    public static JsonArray ListTools() =>
    [
        Tool(
            "get_second_opinion",
            "Alternative perspective on a decision or analysis.",
            new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("prompt"),
                ["properties"] = new JsonObject
                {
                    ["prompt"] = new JsonObject { ["type"] = "string" },
                    ["analysis_depth"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("quick", "balanced", "deep")
                    },
                    ["focus_area"] = new JsonObject { ["type"] = "string" }
                }
            }
        ),
        Tool(
            "analyze_risk",
            "Structured risk analysis with mitigations and indicators.",
            new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("scenario"),
                ["properties"] = new JsonObject
                {
                    ["scenario"] = new JsonObject { ["type"] = "string" },
                    ["context"] = new JsonObject { ["type"] = "string" },
                    ["risk_categories"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" }
                    }
                }
            }
        ),
        Tool(
            "identify_cognitive_biases",
            "Identify likely cognitive biases and practical corrections.",
            new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("analysis"),
                ["properties"] = new JsonObject
                {
                    ["analysis"] = new JsonObject { ["type"] = "string" },
                    ["focus_biases"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" }
                    }
                }
            }
        ),
        Tool(
            "comparative_analysis",
            "Compare options, highlight tradeoffs, and recommend a path.",
            new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("context", "options"),
                ["properties"] = new JsonObject
                {
                    ["context"] = new JsonObject { ["type"] = "string" },
                    ["options"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" }
                    },
                    ["criteria"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" }
                    }
                }
            },
            new JsonObject
            {
                ["ui"] = new JsonObject
                {
                    ["resourceUri"] = "ui://decision-duck/comparative-analysis.html"
                }
            }
        ),
        Tool(
            "list_frameworks",
            "List available decision frameworks.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            }
        ),
        Tool(
            "get_framework",
            "Get one decision framework by ID.",
            new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("framework_id"),
                ["properties"] = new JsonObject
                {
                    ["framework_id"] = new JsonObject { ["type"] = "string" }
                }
            }
        )
    ];

    private static JsonObject Tool(string name, string description, JsonObject inputSchema, JsonObject? metadata = null)
    {
        var tool = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };

        if (metadata is not null)
        {
            tool["metadata"] = metadata;
        }

        return tool;
    }
}

static class ResourceCatalog
{
    private static readonly Dictionary<string, string> Frameworks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["investment"] = "# Investment Assessment Framework\n\nAssess market, team, financials, and risk before committing capital.",
        ["operational-change"] = "# Operational Change Framework\n\nAssess scope, readiness, change impact, and rollout risk.",
        ["strategic-planning"] = "# Strategic Planning Framework\n\nEvaluate market shifts, capabilities, scenarios, and strategic options."
    };

    public static JsonArray ListResources() =>
    [
        new JsonObject
        {
            ["uri"] = "resource://frameworks/investment",
            ["name"] = "Investment Assessment Framework",
            ["mimeType"] = "text/markdown"
        },
        new JsonObject
        {
            ["uri"] = "resource://frameworks/operational-change",
            ["name"] = "Operational Change Framework",
            ["mimeType"] = "text/markdown"
        },
        new JsonObject
        {
            ["uri"] = "resource://frameworks/strategic-planning",
            ["name"] = "Strategic Planning Framework",
            ["mimeType"] = "text/markdown"
        },
        new JsonObject
        {
            ["uri"] = "ui://decision-duck/comparative-analysis.html",
            ["name"] = "Comparative Analysis UI",
            ["mimeType"] = "text/html;profile=mcp-app"
        }
    ];

    public static string ReadResource(string uri) => uri switch
    {
        "resource://frameworks/investment" => Frameworks["investment"],
        "resource://frameworks/operational-change" => Frameworks["operational-change"],
        "resource://frameworks/strategic-planning" => Frameworks["strategic-planning"],
        "ui://decision-duck/comparative-analysis.html" => GetComparativeAnalysisUi(),
        _ => throw new ArgumentException($"Unknown resource URI: {uri}")
    };

    public static string GetMimeType(string uri) => uri switch
    {
        "ui://decision-duck/comparative-analysis.html" => "text/html;profile=mcp-app",
        _ => "text/markdown"
    };

    public static string ListFrameworkNames() =>
        string.Join(Environment.NewLine, Frameworks.Keys.Select(k => "- " + k));

    public static string GetFrameworkById(string frameworkId)
    {
        if (Frameworks.TryGetValue(frameworkId, out var text))
        {
            return text;
        }

        throw new ArgumentException($"Unknown framework_id: {frameworkId}");
    }

        private static string GetComparativeAnalysisUi() => """
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Decision Duck Comparative Analysis</title>
    <style>
        body { font-family: Segoe UI, Arial, sans-serif; margin: 0; padding: 16px; background: #f6f8fb; color: #1f2937; }
        .card { background: white; border-radius: 10px; padding: 16px; box-shadow: 0 2px 12px rgba(15, 23, 42, 0.08); }
        h2 { margin-top: 0; font-size: 18px; }
        .hint { color: #64748b; font-size: 13px; margin-bottom: 12px; }
        pre { white-space: pre-wrap; background: #0b1220; color: #dbeafe; padding: 12px; border-radius: 8px; overflow: auto; }
    </style>
</head>
<body>
    <div class="card">
        <h2>Decision Duck Comparative Analysis</h2>
        <div class="hint">This MCP App view renders the structured tool output from <strong>comparative_analysis</strong>.</div>
        <pre id="payload">Waiting for host-provided tool data...</pre>
    </div>
    <script>
        (function () {
            function render(data) {
                const el = document.getElementById('payload');
                el.textContent = JSON.stringify(data, null, 2);
            }
            window.addEventListener('message', function (event) {
                if (event && event.data) {
                    render(event.data);
                }
            });
        })();
    </script>
</body>
</html>
""";
}
