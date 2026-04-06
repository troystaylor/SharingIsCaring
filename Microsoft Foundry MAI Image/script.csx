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
    // ========================================
    // CONFIGURATION
    // ========================================

    /// <summary>
    /// Application Insights connection string (optional)
    /// Get from: Azure Portal → Application Insights → Overview → Connection String
    /// Leave empty to disable telemetry
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    /// <summary>MCP Server name</summary>
    private const string ServerName = "foundry-mai-image";
    
    /// <summary>MCP Server version</summary>
    private const string ServerVersion = "1.0.0";
    
    /// <summary>MCP Protocol version supported</summary>
    private const string ProtocolVersion = "2025-11-25";

    // ========================================
    // MAIN ENTRY POINT
    // ========================================
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        
        this.Context.Logger.LogInformation($"[{correlationId}] Request received: {this.Context.OperationId}");

        try
        {
            _ = LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                Path = this.Context.Request.RequestUri.AbsolutePath,
                Method = this.Context.Request.Method.Method
            });

            HttpResponseMessage response;
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                    break;

                case "GeneratePhotorealisticImage":
                    response = await HandleGenerateImageAsync(correlationId).ConfigureAwait(false);
                    break;

                case "ChatCompletion":
                    response = await HandleChatCompletionAsync(correlationId).ConfigureAwait(false);
                    break;

                default:
                    response = new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = CreateJsonContent(new JObject
                        {
                            ["error"] = $"Unknown operation: {this.Context.OperationId}"
                        }.ToString())
                    };
                    break;
            }

            return response;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] Error: {ex.Message}");
            _ = LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                Error = ex.Message
            });

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = CreateJsonContent(new JObject
                {
                    ["error"] = ex.Message
                }.ToString())
            };
        }
    }

    // ========================================
    // OPERATION HANDLERS
    // ========================================

    /// <summary>
    /// Handle GeneratePhotorealisticImage REST operation.
    /// </summary>
    private async Task<HttpResponseMessage> HandleGenerateImageAsync(string correlationId)
    {
        _ = LogToAppInsights("ImageGenerationRequest", new
        {
            CorrelationId = correlationId,
            RequestUrl = this.Context.Request.RequestUri.ToString()
        });

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        var statusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("ImageGenerationError", new
            {
                CorrelationId = correlationId,
                StatusCode = statusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);

        var imageCount = (result["data"] as JArray)?.Count ?? 0;
        _ = LogToAppInsights("ImageGenerationResponse", new
        {
            CorrelationId = correlationId,
            StatusCode = statusCode,
            ImageCount = imageCount,
            InputTokens = result["usage"]?["input_tokens"]?.Value<int>() ?? 0,
            OutputTokens = result["usage"]?["output_tokens"]?.Value<int>() ?? 0
        });

        return response;
    }

    /// <summary>
    /// Handle ChatCompletion REST operation (borrowed from parent Foundry connector).
    /// Ensures content values in choices are strings.
    /// </summary>
    private async Task<HttpResponseMessage> HandleChatCompletionAsync(string correlationId)
    {
        _ = LogToAppInsights("ChatCompletionRequest", new
        {
            CorrelationId = correlationId
        });

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);

        // Ensure content values in choices are strings
        var choices = result["choices"] as JArray;
        if (choices != null)
        {
            foreach (var choice in choices)
            {
                var message = choice["message"];
                if (message != null)
                {
                    var content = message["content"];
                    if (content != null && content.Type != JTokenType.String)
                    {
                        message["content"] = content.ToString(Newtonsoft.Json.Formatting.None);
                    }
                }
            }
        }

        response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error: empty request body");
        }

        JObject request;
        try
        {
            request = JObject.Parse(requestBody);
        }
        catch (Exception)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error: invalid JSON");
        }

        var method = request.Value<string>("method");
        var requestId = request["id"];

        _ = LogToAppInsights("McpRequest", new
        {
            CorrelationId = correlationId,
            Method = method,
            HasId = requestId != null
        });

        // Notifications (no id) — return 202 Accepted
        if (requestId == null || requestId.Type == JTokenType.Null)
        {
            switch (method)
            {
                case "notifications/initialized":
                case "notifications/cancelled":
                    return new HttpResponseMessage(HttpStatusCode.Accepted);
                default:
                    return new HttpResponseMessage(HttpStatusCode.Accepted);
            }
        }

        switch (method)
        {
            case "initialize":
                return HandleMcpInitialize(requestId);

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleMcpToolsList(requestId);

            case "tools/call":
                return await HandleMcpToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

            case "completion/complete":
                return CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["completion"] = new JObject { ["values"] = new JArray() }
                });

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, $"Method not found: {method}");
        }
    }

    private HttpResponseMessage HandleMcpInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    // ========================================
    // MCP TOOLS
    // ========================================

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            new JObject
            {
                ["name"] = "generate_image",
                ["description"] = "Generate photorealistic images from text prompts using MAI-Image-2 or other Foundry image models. Returns image URLs or base64 data. Best for marketing content, product visualization, branded assets, and campaign-ready imagery.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "A text description of the desired image. Be specific about lighting, composition, style, and subject matter for best results."
                        },
                        ["model"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Model to use (e.g., MAI-Image-2, gpt-image-1.5). Defaults to the deployment's configured model."
                        },
                        ["size"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Image dimensions: 1024x1024 (square), 1536x1024 (landscape), 1024x1536 (portrait), or auto"
                        },
                        ["quality"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Quality level: low (fast, fewer tokens), medium (balanced), high (best quality, most tokens), or auto"
                        },
                        ["style"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Image style: vivid (bold, dramatic colors) or natural (photorealistic, subtle tones)"
                        },
                        ["response_format"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Return format: url (temporary download link, expires in 60 min) or b64_json (base64 encoded data)"
                        },
                        ["output_format"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "File format: png (best quality, supports transparency), jpeg (smaller size), or webp (modern format)"
                        }
                    },
                    ["required"] = new JArray { "prompt" }
                }
            },
            new JObject
            {
                ["name"] = "chat_completion",
                ["description"] = "Send a chat message to an LLM. Use to describe, interpret, or discuss generated images, suggest prompt improvements, or answer questions about the images in natural language.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The user's message or question"
                        },
                        ["system_prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional system instructions for the model"
                        }
                    },
                    ["required"] = new JArray { "prompt" }
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    /// <summary>
    /// Handle MCP tools/call request
    /// </summary>
    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        _ = LogToAppInsights("McpToolCall", new
        {
            CorrelationId = correlationId,
            ToolName = toolName
        });

        try
        {
            JObject toolResult;

            switch (toolName.ToLowerInvariant())
            {
                case "generate_image":
                    toolResult = await ExecuteGenerateImageToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "chat_completion":
                    toolResult = await ExecuteChatCompletionToolAsync(arguments).ConfigureAwait(false);
                    break;

                default:
                    throw new ArgumentException($"Unknown tool: {toolName}");
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Error: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // MCP TOOL IMPLEMENTATIONS
    // ========================================

    /// <summary>
    /// Execute generate_image MCP tool.
    /// </summary>
    private async Task<JObject> ExecuteGenerateImageToolAsync(JObject arguments)
    {
        var prompt = arguments.Value<string>("prompt");
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("'prompt' is required");
        }

        var body = new JObject
        {
            ["prompt"] = prompt
        };

        var model = arguments.Value<string>("model");
        if (!string.IsNullOrWhiteSpace(model)) body["model"] = model;

        var size = arguments.Value<string>("size");
        if (!string.IsNullOrWhiteSpace(size)) body["size"] = size;

        var quality = arguments.Value<string>("quality");
        if (!string.IsNullOrWhiteSpace(quality)) body["quality"] = quality;

        var style = arguments.Value<string>("style");
        if (!string.IsNullOrWhiteSpace(style)) body["style"] = style;

        var responseFormat = arguments.Value<string>("response_format");
        if (!string.IsNullOrWhiteSpace(responseFormat)) body["response_format"] = responseFormat;

        var outputFormat = arguments.Value<string>("output_format");
        if (!string.IsNullOrWhiteSpace(outputFormat)) body["output_format"] = outputFormat;

        var apiUrl = BuildApiUrl("/images/generations");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, body).ConfigureAwait(false);

        var data = result["data"] as JArray;
        var firstImage = data?.FirstOrDefault() as JObject;

        return new JObject
        {
            ["url"] = firstImage?["url"]?.ToString(),
            ["b64_json"] = firstImage?["b64_json"]?.ToString(),
            ["revised_prompt"] = firstImage?["revised_prompt"]?.ToString(),
            ["image_count"] = data?.Count ?? 0,
            ["usage"] = result["usage"]
        };
    }

    /// <summary>
    /// Execute chat_completion MCP tool (borrowed from parent Foundry connector).
    /// </summary>
    private async Task<JObject> ExecuteChatCompletionToolAsync(JObject arguments)
    {
        var prompt = arguments.Value<string>("prompt");
        var systemPrompt = arguments.Value<string>("system_prompt");

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("'prompt' is required");
        }

        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
        }
        messages.Add(new JObject { ["role"] = "user", ["content"] = prompt });

        var requestBody = new JObject
        {
            ["messages"] = messages,
            ["temperature"] = 0.7,
            ["max_tokens"] = 4096
        };

        var apiUrl = BuildApiUrl("/models/chat/completions");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);

        var content = result["choices"]?[0]?["message"]?["content"]?.ToString();
        return new JObject
        {
            ["response"] = content ?? "No response generated",
            ["usage"] = result["usage"]
        };
    }

    // ========================================
    // API HELPERS
    // ========================================

    private string BuildApiUrl(string path)
    {
        var baseUri = this.Context.Request.RequestUri;
        var host = baseUri.Host;
        return $"https://{host}{path}";
    }

    private async Task<JObject> SendApiRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);
        
        // Forward authorization
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
        // Forward API key
        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("api-key", this.Context.Request.Headers.GetValues("api-key"));
        }
        
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"API request failed ({(int)response.StatusCode}): {content}");
        }

        return JObject.Parse(content);
    }

    // ========================================
    // JSON-RPC HELPERS
    // ========================================

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(responseObj.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message)
    {
        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(responseObj.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    // ========================================
    // TELEMETRY
    // ========================================

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(APP_INSIGHTS_CONNECTION_STRING))
            {
                return;
            }

            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
            {
                return;
            }

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = ServerName,
                ["ServerVersion"] = ServerVersion
            };

            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = Newtonsoft.Json.Linq.JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
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
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Suppress telemetry errors
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring("InstrumentationKey=".Length);
            }
        }
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring("IngestionEndpoint=".Length);
            }
        }
        return "https://dc.services.visualstudio.com";
    }
}
