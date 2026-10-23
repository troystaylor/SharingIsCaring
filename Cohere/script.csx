using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Cohere connector script. One connector, two audiences:
///   - Power Automate / Power Apps call typed REST operations. Chat, Ask and Rerank run through this
///     script to add convenience fields; every other REST operation is a straight pass-through.
///   - Copilot Studio calls InvokeMCP (/mcp), which exposes the same capabilities as MCP tools.
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // CONFIGURATION
    // ========================================

    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private const string ConnectorName = "Cohere";
    private const string ServerName = "cohere";
    private const string ServerVersion = "2.0.0";
    private const string ProtocolVersion = "2025-11-25";
    private const string ClientName = "power-platform-custom-connector";

    private const string DefaultChatModel = "command-a-plus-05-2026";
    private const string DefaultEmbedModel = "embed-v5.0-pro";
    private const string DefaultRerankModel = "rerank-v5.0-pro";
    private const string DefaultParseModel = "parse-v5.0";
    private const string DefaultTranscribeModel = "cohere-transcribe-03-2026";

    // ========================================
    // MAIN ENTRY POINT
    // ========================================

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var operationId = this.Context.OperationId;

        try
        {
            HttpResponseMessage response;
            switch (operationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpAsync(correlationId).ConfigureAwait(false);
                    break;
                case "Chat":
                    response = await HandleChatAsync().ConfigureAwait(false);
                    break;
                case "Ask":
                    response = await HandleAskAsync().ConfigureAwait(false);
                    break;
                case "Rerank":
                    response = await HandleRerankAsync().ConfigureAwait(false);
                    break;
                default:
                    response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
                    break;
            }

            await LogToAppInsightsAsync("RequestCompleted", new Dictionary<string, string>
            {
                ["CorrelationId"] = correlationId,
                ["OperationId"] = operationId,
                ["StatusCode"] = ((int)response.StatusCode).ToString(),
                ["DurationMs"] = (DateTime.UtcNow - startTime).TotalMilliseconds.ToString("F0")
            }).ConfigureAwait(false);

            return response;
        }
        catch (ArgumentException ex)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = ToJsonContent(new JObject { ["message"] = ex.Message })
            };
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("RequestError", new Dictionary<string, string>
            {
                ["CorrelationId"] = correlationId,
                ["OperationId"] = operationId,
                ["ErrorType"] = ex.GetType().Name,
                ["ErrorMessage"] = ex.Message,
                ["StackTrace"] = ex.StackTrace ?? string.Empty
            }).ConfigureAwait(false);
            throw;
        }
    }

    // ========================================
    // REST OPERATIONS
    // ========================================

    /// <summary>Chat: forces non-streaming and adds a joined "text" field to the response.</summary>
    private async Task<HttpResponseMessage> HandleChatAsync()
    {
        var body = JObject.Parse(await ReadRequestBodyAsync().ConfigureAwait(false));
        body["stream"] = false;
        this.Context.Request.Content = ToJsonContent(body);

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var result = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        result["text"] = ExtractChatText(result);
        response.Content = ToJsonContent(result);
        return response;
    }

    /// <summary>Ask: turns a single prompt into a v2/chat request and returns a flat result.</summary>
    private async Task<HttpResponseMessage> HandleAskAsync()
    {
        var args = JObject.Parse(await ReadRequestBodyAsync().ConfigureAwait(false));
        var chatBody = BuildChatBody(args);

        this.Context.Request.RequestUri = new UriBuilder(this.Context.Request.RequestUri) { Path = "/v2/chat" }.Uri;
        this.Context.Request.Content = ToJsonContent(chatBody);

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var chat = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        response.Content = ToJsonContent(ToAskResult(chat));
        return response;
    }

    /// <summary>Rerank: adds the original document text and, for Context Focus, the text of each span.</summary>
    private async Task<HttpResponseMessage> HandleRerankAsync()
    {
        var bodyString = await ReadRequestBodyAsync().ConfigureAwait(false);
        var documents = JObject.Parse(bodyString)["documents"] as JArray;
        this.Context.Request.Content = new StringContent(bodyString, Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var result = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        EnrichRerankResults(result, documents);
        response.Content = ToJsonContent(result);
        return response;
    }

    // ========================================
    // SHARED CHAT / RERANK HELPERS
    // ========================================

    private JObject BuildChatBody(JObject args)
    {
        var prompt = args.Value<string>("prompt");
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("'prompt' is required");
        }

        var messages = new JArray();
        var systemPrompt = args.Value<string>("system_prompt");
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
        }
        messages.Add(new JObject { ["role"] = "user", ["content"] = prompt });

        var body = new JObject
        {
            ["model"] = NonEmpty(args.Value<string>("model")) ?? DefaultChatModel,
            ["messages"] = messages,
            ["stream"] = false
        };

        var documents = args["documents"] as JArray;
        if (documents != null && documents.Count > 0)
        {
            body["documents"] = documents;
        }
        CopyIfPresent(args, body, "temperature");
        CopyIfPresent(args, body, "max_tokens");
        return body;
    }

    private static string ExtractChatText(JObject chat)
    {
        var sb = new StringBuilder();
        var content = chat["message"]?["content"] as JArray;
        if (content != null)
        {
            foreach (var item in content)
            {
                if (item.Value<string>("type") == "text")
                {
                    sb.Append(item.Value<string>("text"));
                }
            }
        }
        return sb.ToString();
    }

    private static JObject ToAskResult(JObject chat)
    {
        return new JObject
        {
            ["id"] = chat["id"],
            ["text"] = ExtractChatText(chat),
            ["finish_reason"] = chat["finish_reason"],
            ["citations"] = chat["message"]?["citations"] ?? new JArray(),
            ["usage"] = chat["usage"]
        };
    }

    private static void EnrichRerankResults(JObject result, JArray documents)
    {
        var results = result["results"] as JArray;
        if (results == null || documents == null)
        {
            return;
        }

        foreach (var item in results)
        {
            var index = item.Value<int>("index");
            if (index < 0 || index >= documents.Count)
            {
                continue;
            }

            var text = documents[index].ToString();
            item["document"] = text;

            var spans = item["relevant_spans"] as JArray;
            if (spans != null)
            {
                foreach (var span in spans)
                {
                    span["text"] = CodepointSlice(text, span.Value<int>("start"), span.Value<int>("end"));
                }
            }
        }
    }

    // Cohere reports spans in Unicode code points, not UTF-16 code units.
    private static string CodepointSlice(string s, int start, int end)
    {
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        int cp = 0, from = -1, to = s.Length;
        for (int i = 0; i < s.Length; i++)
        {
            if (cp == start) from = i;
            if (cp == end) { to = i; break; }
            cp++;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
        }
        return from < 0 ? string.Empty : s.Substring(from, to - from);
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId)
    {
        JToken requestId = null;
        try
        {
            var body = await ReadRequestBodyAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
            }

            JObject request;
            try
            {
                request = JObject.Parse(body);
            }
            catch (JsonException)
            {
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
            }

            var method = request.Value<string>("method") ?? string.Empty;
            requestId = request["id"];

            switch (method)
            {
                case "initialize":
                    return HandleMcpInitialize(request, requestId);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "ping":
                case "logging/setLevel":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                case "tools/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = BuildToolList() });

                case "tools/call":
                    return await HandleMcpToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);

                case "resources/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });

                case "prompts/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

                case "completion/complete":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleMcpInitialize(JObject request, JToken requestId)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion;

        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
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
                ["title"] = ConnectorName,
                ["description"] = "Cohere Command, Embed, Rerank, Parse and Transcribe models, plus batches and datasets"
            }
        });
    }

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var parameters = request["params"] as JObject;
        var toolName = parameters?.Value<string>("name");
        var arguments = parameters?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");
        }

        var startTime = DateTime.UtcNow;
        try
        {
            var result = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);

            await LogToAppInsightsAsync("McpToolCompleted", new Dictionary<string, string>
            {
                ["CorrelationId"] = correlationId,
                ["Tool"] = toolName,
                ["Success"] = "true",
                ["DurationMs"] = (DateTime.UtcNow - startTime).TotalMilliseconds.ToString("F0")
            }).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(requestId, ToolResult(result.ToString(Newtonsoft.Json.Formatting.None), false));
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("McpToolFailed", new Dictionary<string, string>
            {
                ["CorrelationId"] = correlationId,
                ["Tool"] = toolName,
                ["Success"] = "false",
                ["ErrorType"] = ex.GetType().Name,
                ["ErrorMessage"] = ex.Message,
                ["StackTrace"] = ex.StackTrace ?? string.Empty
            }).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(requestId, ToolResult("Tool execution failed: " + ex.Message, true));
        }
    }

    private static JObject ToolResult(string text, bool isError)
    {
        return new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError
        };
    }

    // ========================================
    // MCP TOOL DEFINITIONS
    // ========================================

    private static JObject Str(string description, params string[] allowed)
    {
        var s = new JObject { ["type"] = "string", ["description"] = description };
        if (allowed.Length > 0) s["enum"] = new JArray(allowed);
        return s;
    }

    private static JObject Int(string description) => new JObject { ["type"] = "integer", ["description"] = description };
    private static JObject Num(string description) => new JObject { ["type"] = "number", ["description"] = description };
    private static JObject Bool(string description) => new JObject { ["type"] = "boolean", ["description"] = description };

    private static JObject ArrOf(string itemType, string description) => new JObject
    {
        ["type"] = "array",
        ["items"] = new JObject { ["type"] = itemType },
        ["description"] = description
    };

    private static JObject Tool(string name, string description, JObject properties, params string[] required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties ?? new JObject(),
                ["required"] = new JArray(required)
            }
        };
    }

    private static JArray BuildToolList()
    {
        return new JArray
        {
            Tool("cohere_chat",
                "Ask a Cohere Command model a question or give it a task and get text back. Pass documents to ground the answer; the response includes citations to them.",
                new JObject
                {
                    ["prompt"] = Str("The question or instruction."),
                    ["system_prompt"] = Str("Optional instructions that set the model's behavior."),
                    ["documents"] = ArrOf("string", "Optional text passages the model can ground its answer in and cite."),
                    ["model"] = Str("Chat model. Defaults to " + DefaultChatModel + ". Use cohere_list_models with endpoint=chat to see options."),
                    ["temperature"] = Num("Randomness from 0 to 1. Default 0.3."),
                    ["max_tokens"] = Int("Maximum output tokens.")
                }, "prompt"),

            Tool("cohere_embed",
                "Create embeddings for a list of texts. Use search_document for content you will store and search_query for the query.",
                new JObject
                {
                    ["texts"] = ArrOf("string", "Up to 96 strings to embed."),
                    ["input_type"] = Str("What the embeddings are for. Default search_document.", "search_document", "search_query", "classification", "clustering"),
                    ["model"] = Str("Embedding model. Defaults to " + DefaultEmbedModel + "."),
                    ["output_dimension"] = Int("Vector size: 256, 512, 768, 1024, 1536 or 2048 (embed-v5.0 models).")
                }, "texts"),

            Tool("cohere_rerank",
                "Order documents by relevance to a query. Use after retrieval and before sending context to an LLM. Optionally filter by score or return the most relevant passages of each document.",
                new JObject
                {
                    ["query"] = Str("The search query."),
                    ["documents"] = ArrOf("string", "Texts to rank against the query. Up to 1,000 recommended."),
                    ["top_n"] = Int("Return only the top N results."),
                    ["min_score"] = Num("Drop results scoring below this value (0 to 1)."),
                    ["include_relevant_spans"] = Bool("Also return the most relevant passages within each document (Rerank v5 models)."),
                    ["max_tokens_per_doc"] = Int("Truncate each document to this many tokens. Default 4096."),
                    ["model"] = Str("Rerank model. Defaults to " + DefaultRerankModel + ".")
                }, "query", "documents"),

            Tool("cohere_parse_document",
                "Extract structured markdown or blocks from an image of a document page (form, invoice, report). PDFs are not supported.",
                new JObject
                {
                    ["image_url"] = Str("Public http(s) image URL or a base64 data URI. Max 20 MB."),
                    ["output_format"] = Str("markdown (default) or blocks.", "markdown", "blocks"),
                    ["model"] = Str("Parse model. Defaults to " + DefaultParseModel + ".")
                }, "image_url"),

            Tool("cohere_transcribe_audio",
                "Transcribe an audio file (flac, mp3, mpeg, mpga, ogg, wav). Provide audio_url or audio_base64.",
                new JObject
                {
                    ["language"] = Str("ISO-639-1 language code of the audio, for example en."),
                    ["audio_url"] = Str("https URL of the audio file."),
                    ["audio_base64"] = Str("Base64-encoded audio. Requires file_name."),
                    ["file_name"] = Str("File name with extension, for example call.mp3. The extension sets the audio format."),
                    ["model"] = Str("Transcription model. Defaults to " + DefaultTranscribeModel + "."),
                    ["temperature"] = Num("Sampling temperature from 0 to 1.")
                }, "language"),

            Tool("cohere_tokenize", "Split text into tokens using a model's tokenizer. Useful for counting tokens.",
                new JObject
                {
                    ["text"] = Str("1 to 65,536 characters."),
                    ["model"] = Str("Model whose tokenizer is used.")
                }, "text", "model"),

            Tool("cohere_detokenize", "Turn token IDs back into text.",
                new JObject
                {
                    ["tokens"] = ArrOf("integer", "Token IDs."),
                    ["model"] = Str("Model whose tokenizer is used.")
                }, "tokens", "model"),

            Tool("cohere_list_models", "List Cohere models, optionally only those usable with one endpoint.",
                new JObject
                {
                    ["endpoint"] = Str("Only models compatible with this endpoint.", "chat", "embed", "classify", "summarize", "rerank", "rate", "generate"),
                    ["default_only"] = Bool("Only the default model for the endpoint. Requires endpoint."),
                    ["page_size"] = Int("Models per page, 1 to 1000. Default 20."),
                    ["page_token"] = Str("Token from a previous response's next_page_token.")
                }),

            Tool("cohere_get_model", "Get details about one model: context length, endpoints, features and sampling defaults.",
                new JObject { ["model"] = Str("Model name.") }, "model"),

            Tool("cohere_check_api_key", "Check whether the connection's Cohere API key is valid.", new JObject()),

            Tool("cohere_list_batches", "List batch jobs.",
                new JObject
                {
                    ["page_size"] = Int("Batches per page, up to 100. Default 50."),
                    ["page_token"] = Str("Token from a previous response's next_page_token."),
                    ["order_by"] = Str("Sort order.", "created_at", "updated_at")
                }),

            Tool("cohere_get_batch", "Get a batch job and its status.",
                new JObject { ["id"] = Str("Batch ID.") }, "id"),

            Tool("cohere_create_batch", "Run a model over an uploaded dataset of requests.",
                new JObject
                {
                    ["name"] = Str("Batch name."),
                    ["input_dataset_id"] = Str("ID of the dataset the batch reads from. Use cohere_list_datasets to find it."),
                    ["model"] = Str("Model the batch uses.")
                }, "name", "input_dataset_id", "model"),

            Tool("cohere_cancel_batch", "Cancel a running batch job.",
                new JObject { ["id"] = Str("Batch ID.") }, "id"),

            Tool("cohere_list_datasets", "List datasets.",
                new JObject
                {
                    ["dataset_type"] = Str("Filter by dataset type."),
                    ["validation_status"] = Str("Filter by validation status.", "unknown", "queued", "processing", "failed", "validated", "skipped"),
                    ["limit"] = Int("Maximum number of results."),
                    ["offset"] = Int("Offset to start from.")
                })
        };
    }

    // ========================================
    // MCP TOOL EXECUTION
    // ========================================

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject a)
    {
        switch (toolName)
        {
            case "cohere_chat":
            {
                var chat = await CallApiAsync(HttpMethod.Post, "/v2/chat", BuildChatBody(a)).ConfigureAwait(false);
                return ToAskResult(chat);
            }

            case "cohere_embed":
            {
                var texts = a["texts"] as JArray;
                if (texts == null || texts.Count == 0) throw new ArgumentException("'texts' must be a non-empty array");

                var body = new JObject
                {
                    ["model"] = NonEmpty(a.Value<string>("model")) ?? DefaultEmbedModel,
                    ["input_type"] = NonEmpty(a.Value<string>("input_type")) ?? "search_document",
                    ["texts"] = texts,
                    ["embedding_types"] = new JArray("float")
                };
                CopyIfPresent(a, body, "output_dimension");
                return await CallApiAsync(HttpMethod.Post, "/v2/embed", body).ConfigureAwait(false);
            }

            case "cohere_rerank":
                return await ToolRerankAsync(a).ConfigureAwait(false);

            case "cohere_parse_document":
            {
                var imageUrl = RequireString(a, "image_url");
                var body = new JObject
                {
                    ["model"] = NonEmpty(a.Value<string>("model")) ?? DefaultParseModel,
                    ["document"] = new JObject { ["type"] = "image_url", ["image_url"] = imageUrl }
                };
                CopyIfPresent(a, body, "output_format");
                return await CallApiAsync(HttpMethod.Post, "/v2/parse", body).ConfigureAwait(false);
            }

            case "cohere_transcribe_audio":
                return await ToolTranscribeAsync(a).ConfigureAwait(false);

            case "cohere_tokenize":
                return await CallApiAsync(HttpMethod.Post, "/v1/tokenize", new JObject
                {
                    ["text"] = RequireString(a, "text"),
                    ["model"] = RequireString(a, "model")
                }).ConfigureAwait(false);

            case "cohere_detokenize":
            {
                var tokens = a["tokens"] as JArray;
                if (tokens == null || tokens.Count == 0) throw new ArgumentException("'tokens' must be a non-empty array");
                return await CallApiAsync(HttpMethod.Post, "/v1/detokenize", new JObject
                {
                    ["tokens"] = tokens,
                    ["model"] = RequireString(a, "model")
                }).ConfigureAwait(false);
            }

            case "cohere_list_models":
                return await CallApiAsync(HttpMethod.Get, "/v1/models", null,
                    Query(a, "endpoint", "default_only", "page_size", "page_token")).ConfigureAwait(false);

            case "cohere_get_model":
                return await CallApiAsync(HttpMethod.Get, "/v1/models/" + Uri.EscapeDataString(RequireString(a, "model"))).ConfigureAwait(false);

            case "cohere_check_api_key":
                return await CallApiAsync(HttpMethod.Post, "/v1/check-api-key", new JObject()).ConfigureAwait(false);

            case "cohere_list_batches":
                return await CallApiAsync(HttpMethod.Get, "/v2/batches", null,
                    Query(a, "page_size", "page_token", "order_by")).ConfigureAwait(false);

            case "cohere_get_batch":
                return await CallApiAsync(HttpMethod.Get, "/v2/batches/" + Uri.EscapeDataString(RequireString(a, "id"))).ConfigureAwait(false);

            case "cohere_create_batch":
                return await CallApiAsync(HttpMethod.Post, "/v2/batches", new JObject
                {
                    ["name"] = RequireString(a, "name"),
                    ["input_dataset_id"] = RequireString(a, "input_dataset_id"),
                    ["model"] = RequireString(a, "model")
                }).ConfigureAwait(false);

            case "cohere_cancel_batch":
                return await CallApiAsync(HttpMethod.Post, "/v2/batches/" + Uri.EscapeDataString(RequireString(a, "id")) + ":cancel", new JObject()).ConfigureAwait(false);

            case "cohere_list_datasets":
            {
                var query = Query(a, "limit", "offset");
                if (!string.IsNullOrWhiteSpace(a.Value<string>("dataset_type"))) query["datasetType"] = a.Value<string>("dataset_type");
                if (!string.IsNullOrWhiteSpace(a.Value<string>("validation_status"))) query["validationStatus"] = a.Value<string>("validation_status");
                return await CallApiAsync(HttpMethod.Get, "/v1/datasets", null, query).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown tool: " + toolName);
        }
    }

    private async Task<JObject> ToolRerankAsync(JObject a)
    {
        var query = RequireString(a, "query");
        var documents = a["documents"] as JArray;
        if (documents == null || documents.Count == 0) throw new ArgumentException("'documents' must be a non-empty array");

        var body = new JObject
        {
            ["model"] = NonEmpty(a.Value<string>("model")) ?? DefaultRerankModel,
            ["query"] = query,
            ["documents"] = documents
        };
        CopyIfPresent(a, body, "top_n");
        CopyIfPresent(a, body, "max_tokens_per_doc");
        if (a.Value<bool?>("include_relevant_spans") == true)
        {
            body["relevant_spans"] = new JObject { ["mode"] = "enabled" };
        }

        var result = await CallApiAsync(HttpMethod.Post, "/v2/rerank", body).ConfigureAwait(false);
        EnrichRerankResults(result, documents);

        var minScore = a.Value<double?>("min_score");
        if (minScore.HasValue)
        {
            var kept = new JArray((result["results"] as JArray ?? new JArray())
                .Where(r => r.Value<double>("relevance_score") >= minScore.Value));
            result["results"] = kept;
            result["total_input"] = documents.Count;
            result["total_passed"] = kept.Count;
        }
        return result;
    }

    private async Task<JObject> ToolTranscribeAsync(JObject a)
    {
        var language = RequireString(a, "language");
        var fileName = NonEmpty(a.Value<string>("file_name"));
        var base64 = NonEmpty(a.Value<string>("audio_base64"));
        var audioUrl = NonEmpty(a.Value<string>("audio_url"));
        byte[] audio;

        if (base64 != null)
        {
            if (fileName == null) throw new ArgumentException("'file_name' is required with audio_base64; its extension sets the audio format");
            var comma = base64.IndexOf(',');
            if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) base64 = base64.Substring(comma + 1);
            audio = Convert.FromBase64String(base64);
        }
        else if (audioUrl != null)
        {
            Uri uri;
            if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("'audio_url' must be an https URL");
            }

            var download = await this.Context.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), this.CancellationToken).ConfigureAwait(false);
            if (!download.IsSuccessStatusCode) throw new Exception("Could not download audio_url (" + (int)download.StatusCode + ")");
            audio = await download.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            fileName = fileName ?? System.IO.Path.GetFileName(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("'file_name' is required when the URL has no file name");
        }
        else
        {
            throw new ArgumentException("Provide 'audio_url' or 'audio_base64'");
        }

        var form = new MultipartFormDataContent();
        form.Add(new StringContent(NonEmpty(a.Value<string>("model")) ?? DefaultTranscribeModel), "model");
        form.Add(new StringContent(language), "language");
        var temperature = a.Value<double?>("temperature");
        if (temperature.HasValue)
        {
            form.Add(new StringContent(temperature.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), "temperature");
        }
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(AudioMimeType(fileName));
        form.Add(file, "file", fileName);

        var request = CreateApiRequest(HttpMethod.Post, "/v2/audio/transcriptions", null);
        request.Content = form;
        return await SendApiRequestAsync(request).ConfigureAwait(false);
    }

    private static string AudioMimeType(string fileName)
    {
        switch (System.IO.Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".flac": return "audio/flac";
            case ".mp3":
            case ".mpeg":
            case ".mpga": return "audio/mpeg";
            case ".ogg": return "audio/ogg";
            case ".wav": return "audio/wav";
            default: return "application/octet-stream";
        }
    }

    // ========================================
    // COHERE API HELPERS
    // ========================================

    private HttpRequestMessage CreateApiRequest(HttpMethod method, string path, IDictionary<string, string> query)
    {
        var url = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority) + path;
        if (query != null && query.Count > 0)
        {
            url += "?" + string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        }

        var request = new HttpRequestMessage(method, url);
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        request.Headers.TryAddWithoutValidation("X-Client-Name", ClientName);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<JObject> CallApiAsync(HttpMethod method, string path, JObject body = null, IDictionary<string, string> query = null)
    {
        var request = CreateApiRequest(method, path, query);
        if (body != null)
        {
            request.Content = ToJsonContent(body);
        }
        return await SendApiRequestAsync(request).ConfigureAwait(false);
    }

    private async Task<JObject> SendApiRequestAsync(HttpRequestMessage request)
    {
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string message = content;
            try { message = JObject.Parse(content).Value<string>("message") ?? content; } catch { }
            throw new Exception("Cohere API error " + (int)response.StatusCode + ": " + message);
        }

        return string.IsNullOrWhiteSpace(content) ? new JObject() : JObject.Parse(content);
    }

    // ========================================
    // SMALL UTILITIES
    // ========================================

    private async Task<string> ReadRequestBodyAsync()
    {
        if (this.Context.Request.Content == null) return string.Empty;
        return await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private static StringContent ToJsonContent(JObject json)
    {
        return new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
    }

    private static string NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string RequireString(JObject args, string key)
    {
        var value = args.Value<string>(key);
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("'" + key + "' is required");
        return value;
    }

    private static void CopyIfPresent(JObject source, JObject target, string key)
    {
        var value = source[key];
        if (value != null && value.Type != JTokenType.Null)
        {
            target[key] = value;
        }
    }

    private static Dictionary<string, string> Query(JObject args, params string[] keys)
    {
        var query = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var value = args[key];
            if (value == null || value.Type == JTokenType.Null) continue;
            query[key] = value.Type == JTokenType.Boolean
                ? value.ToObject<bool>().ToString().ToLowerInvariant()
                : value.ToString();
        }
        return query;
    }

    // ========================================
    // JSON-RPC HELPERS
    // ========================================

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var payload = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = ToJsonContent(payload) };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;

        var payload = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = ToJsonContent(payload) };
    }

    // ========================================
    // APPLICATION INSIGHTS TELEMETRY
    // ========================================

    private async Task LogToAppInsightsAsync(string eventName, IDictionary<string, string> properties = null)
    {
        if (!APP_INSIGHTS_ENABLED
            || string.IsNullOrEmpty(APP_INSIGHTS_KEY)
            || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
        {
            return;
        }

        try
        {
            var props = new Dictionary<string, string>(properties ?? new Dictionary<string, string>())
            {
                ["Connector"] = ConnectorName,
                ["ServerVersion"] = ServerVersion
            };

            var telemetryData = new
            {
                name = "Microsoft.ApplicationInsights.Event",
                time = DateTime.UtcNow.ToString("O"),
                iKey = APP_INSIGHTS_KEY,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = props }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(JsonConvert.SerializeObject(telemetryData), Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Telemetry must never break the operation.
        }
    }
}
