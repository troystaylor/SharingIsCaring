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
    private const string ServerName = "foundry-phi4";
    
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

                case "ReasonWithVision":
                    response = await HandleReasonWithVisionAsync(correlationId).ConfigureAwait(false);
                    break;

                case "ChatMultimodal":
                    response = await HandleChatMultimodalAsync(correlationId).ConfigureAwait(false);
                    break;

                case "ChatMini":
                    response = await HandleChatMiniAsync(correlationId).ConfigureAwait(false);
                    break;

                default:
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
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
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message
            });
            throw;
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            _ = LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                DurationMs = duration.TotalMilliseconds
            });
        }
    }

    // ========================================
    // OPERATION HANDLERS
    // ========================================

    /// <summary>
    /// Handles ReasonWithVision requests.
    /// Builds a multimodal content array from prompt + image_url, sends to
    /// Phi-4-Reasoning-Vision-15B, and extracts reasoning + answer from the response.
    /// </summary>
    private async Task<HttpResponseMessage> HandleReasonWithVisionAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);

        var prompt = body.Value<string>("prompt");
        var imageUrl = body.Value<string>("image_url");
        var systemPrompt = body.Value<string>("system_prompt");
        var temperature = body["temperature"]?.Value<float>() ?? 0.7f;
        var maxTokens = body["max_tokens"]?.Value<int>() ?? 4096;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"prompt is required\"}")
            };
        }

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"image_url is required\"}")
            };
        }

        _ = LogToAppInsights("ReasonWithVisionRequest", new
        {
            CorrelationId = correlationId,
            PromptLength = prompt.Length,
            HasSystemPrompt = !string.IsNullOrWhiteSpace(systemPrompt)
        });

        // Build multimodal content array
        var userContent = new JArray
        {
            new JObject { ["type"] = "text", ["text"] = prompt },
            new JObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JObject { ["url"] = imageUrl }
            }
        };

        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
        }
        messages.Add(new JObject { ["role"] = "user", ["content"] = userContent });

        var chatRequest = new JObject
        {
            ["model"] = "Phi-4-Reasoning-Vision-15B",
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens
        };

        // Rewrite URL to the AI Services chat completions endpoint
        var uri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/models/chat/completions"
        }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(
            chatRequest.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("ReasonWithVisionError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);

        // Extract reasoning and answer from the response
        var message = result["choices"]?[0]?["message"];
        var reasoning = message?["reasoning_content"]?.ToString() ?? "";
        var answer = message?["content"]?.ToString() ?? "";

        // If no separate reasoning_content, try to split from content
        if (string.IsNullOrEmpty(reasoning) && !string.IsNullOrEmpty(answer))
        {
            var thinkMatch = System.Text.RegularExpressions.Regex.Match(
                answer,
                @"<think>(.*?)</think>(.*)",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (thinkMatch.Success)
            {
                reasoning = thinkMatch.Groups[1].Value.Trim();
                answer = thinkMatch.Groups[2].Value.Trim();
            }
        }

        var formattedResponse = new JObject
        {
            ["reasoning"] = reasoning,
            ["answer"] = answer,
            ["model"] = result["model"],
            ["usage"] = result["usage"]
        };

        _ = LogToAppInsights("ReasonWithVisionResult", new
        {
            CorrelationId = correlationId,
            ReasoningLength = reasoning.Length,
            AnswerLength = answer.Length,
            PromptTokens = result["usage"]?["prompt_tokens"]?.Value<int>() ?? 0,
            CompletionTokens = result["usage"]?["completion_tokens"]?.Value<int>() ?? 0
        });

        response.Content = CreateJsonContent(formattedResponse.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    /// <summary>
    /// Handles ChatMultimodal requests.
    /// Builds a multimodal content array from prompt + optional image/audio,
    /// sends to Phi-4-multimodal-instruct.
    /// </summary>
    private async Task<HttpResponseMessage> HandleChatMultimodalAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);

        var prompt = body.Value<string>("prompt");
        var imageUrl = body.Value<string>("image_url");
        var audioUrl = body.Value<string>("audio_url");
        var systemPrompt = body.Value<string>("system_prompt");
        var temperature = body["temperature"]?.Value<float>() ?? 0.7f;
        var maxTokens = body["max_tokens"]?.Value<int>() ?? 4096;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"prompt is required\"}")
            };
        }

        _ = LogToAppInsights("ChatMultimodalRequest", new
        {
            CorrelationId = correlationId,
            PromptLength = prompt.Length,
            HasImage = !string.IsNullOrWhiteSpace(imageUrl),
            HasAudio = !string.IsNullOrWhiteSpace(audioUrl),
            HasSystemPrompt = !string.IsNullOrWhiteSpace(systemPrompt)
        });

        // Build content — text-only or multimodal depending on inputs
        JToken userContent;
        var hasMedia = !string.IsNullOrWhiteSpace(imageUrl) || !string.IsNullOrWhiteSpace(audioUrl);

        if (hasMedia)
        {
            var contentArray = new JArray
            {
                new JObject { ["type"] = "text", ["text"] = prompt }
            };

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                contentArray.Add(new JObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JObject { ["url"] = imageUrl }
                });
            }

            if (!string.IsNullOrWhiteSpace(audioUrl))
            {
                contentArray.Add(new JObject
                {
                    ["type"] = "audio_url",
                    ["audio_url"] = new JObject { ["url"] = audioUrl }
                });
            }

            userContent = contentArray;
        }
        else
        {
            userContent = prompt;
        }

        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
        }
        messages.Add(new JObject { ["role"] = "user", ["content"] = userContent });

        var chatRequest = new JObject
        {
            ["model"] = "Phi-4-multimodal-instruct",
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens
        };

        var uri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/models/chat/completions"
        }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(
            chatRequest.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("ChatMultimodalError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
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

        _ = LogToAppInsights("ChatMultimodalResult", new
        {
            CorrelationId = correlationId,
            PromptTokens = result["usage"]?["prompt_tokens"]?.Value<int>() ?? 0,
            CompletionTokens = result["usage"]?["completion_tokens"]?.Value<int>() ?? 0
        });

        response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    /// <summary>
    /// Handles ChatMini requests.
    /// Forwards the messages array to Phi-4-mini-instruct. Ensures content is returned as a string.
    /// </summary>
    private async Task<HttpResponseMessage> HandleChatMiniAsync(string correlationId)
    {
        _ = LogToAppInsights("ChatMiniRequest", new
        {
            CorrelationId = correlationId,
            RequestUrl = this.Context.Request.RequestUri.ToString()
        });

        // Read the body and inject the model name
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);
        body["model"] = "Phi-4-mini-instruct";

        // Rewrite URL to /models/chat/completions
        var uri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/models/chat/completions"
        }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(
            body.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("ChatMiniError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
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

        _ = LogToAppInsights("ChatMiniResult", new
        {
            CorrelationId = correlationId,
            PromptTokens = result["usage"]?["prompt_tokens"]?.Value<int>() ?? 0,
            CompletionTokens = result["usage"]?["completion_tokens"]?.Value<int>() ?? 0
        });

        response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    /// <summary>
    /// Pass-through handler - forwards the request and returns the response unchanged.
    /// </summary>
    private async Task<HttpResponseMessage> HandlePassthroughAsync()
    {
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);
        return response;
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
    {
        string body = null;
        JObject request = null;
        string method = null;
        JToken requestId = null;

        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            
            if (string.IsNullOrWhiteSpace(body))
            {
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
            }

            try
            {
                request = JObject.Parse(body);
            }
            catch (JsonException)
            {
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
            }

            method = request.Value<string>("method") ?? string.Empty;
            requestId = request["id"];

            _ = LogToAppInsights("McpRequestReceived", new
            {
                CorrelationId = correlationId,
                Method = method
            });

            switch (method)
            {
                case "initialize":
                    return HandleMcpInitialize(request, requestId);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                case "tools/list":
                    return HandleMcpToolsList(requestId);

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

                case "logging/setLevel":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

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

        var result = new JObject
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
                ["title"] = "Microsoft Foundry Phi-4",
                ["description"] = "Vision reasoning, multimodal chat, and lightweight text chat using the Phi-4 model family"
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    // ========================================
    // MCP TOOLS LIST
    // ========================================

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            new JObject
            {
                ["name"] = "reason_with_vision",
                ["description"] = "Send an image and text prompt to Phi-4-Reasoning-Vision for visual reasoning. Returns step-by-step reasoning and a final answer. Excels at math, science, UI understanding, document analysis, and diagram interpretation.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The question or instruction about the image"
                        },
                        ["image_url"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "URL or base64 data URI of the image to analyze"
                        },
                        ["system_prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional system instructions for the model"
                        }
                    },
                    ["required"] = new JArray { "prompt", "image_url" }
                }
            },
            new JObject
            {
                ["name"] = "chat_multimodal",
                ["description"] = "Send text, images, and audio to Phi-4-multimodal. Processes multiple input types simultaneously. Supports 23 languages.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The text message or question"
                        },
                        ["image_url"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional URL or base64 data URI of an image"
                        },
                        ["audio_url"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional URL or base64 data URI of an audio file"
                        },
                        ["system_prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional system instructions for the model"
                        }
                    },
                    ["required"] = new JArray { "prompt" }
                }
            },
            new JObject
            {
                ["name"] = "chat_mini",
                ["description"] = "Send a text message to Phi-4-mini for fast, lightweight chat. 3.8B parameter model with 131K context and 23 language support.",
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

    // ========================================
    // MCP TOOLS/CALL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");
        }

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
                case "reason_with_vision":
                    toolResult = await ExecuteReasonWithVisionToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "chat_multimodal":
                    toolResult = await ExecuteChatMultimodalToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "chat_mini":
                    toolResult = await ExecuteChatMiniToolAsync(arguments).ConfigureAwait(false);
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
                        ["text"] = $"Tool execution failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // MCP TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<JObject> ExecuteReasonWithVisionToolAsync(JObject arguments)
    {
        var prompt = arguments.Value<string>("prompt");
        var imageUrl = arguments.Value<string>("image_url");
        var systemPrompt = arguments.Value<string>("system_prompt");

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("'prompt' is required");
        }

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new ArgumentException("'image_url' is required");
        }

        var userContent = new JArray
        {
            new JObject { ["type"] = "text", ["text"] = prompt },
            new JObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JObject { ["url"] = imageUrl }
            }
        };

        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
        }
        messages.Add(new JObject { ["role"] = "user", ["content"] = userContent });

        var requestBody = new JObject
        {
            ["model"] = "Phi-4-Reasoning-Vision-15B",
            ["messages"] = messages,
            ["temperature"] = 0.7,
            ["max_tokens"] = 4096
        };

        var apiUrl = BuildApiUrl("/models/chat/completions");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);

        var message = result["choices"]?[0]?["message"];
        var reasoning = message?["reasoning_content"]?.ToString() ?? "";
        var answer = message?["content"]?.ToString() ?? "";

        // If no separate reasoning_content, try to split <think> tags
        if (string.IsNullOrEmpty(reasoning) && !string.IsNullOrEmpty(answer))
        {
            var thinkMatch = System.Text.RegularExpressions.Regex.Match(
                answer,
                @"<think>(.*?)</think>(.*)",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (thinkMatch.Success)
            {
                reasoning = thinkMatch.Groups[1].Value.Trim();
                answer = thinkMatch.Groups[2].Value.Trim();
            }
        }

        return new JObject
        {
            ["reasoning"] = reasoning,
            ["answer"] = answer,
            ["model"] = result["model"],
            ["usage"] = result["usage"]
        };
    }

    private async Task<JObject> ExecuteChatMultimodalToolAsync(JObject arguments)
    {
        var prompt = arguments.Value<string>("prompt");
        var imageUrl = arguments.Value<string>("image_url");
        var audioUrl = arguments.Value<string>("audio_url");
        var systemPrompt = arguments.Value<string>("system_prompt");

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("'prompt' is required");
        }

        JToken userContent;
        var hasMedia = !string.IsNullOrWhiteSpace(imageUrl) || !string.IsNullOrWhiteSpace(audioUrl);

        if (hasMedia)
        {
            var contentArray = new JArray
            {
                new JObject { ["type"] = "text", ["text"] = prompt }
            };

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                contentArray.Add(new JObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JObject { ["url"] = imageUrl }
                });
            }

            if (!string.IsNullOrWhiteSpace(audioUrl))
            {
                contentArray.Add(new JObject
                {
                    ["type"] = "audio_url",
                    ["audio_url"] = new JObject { ["url"] = audioUrl }
                });
            }

            userContent = contentArray;
        }
        else
        {
            userContent = prompt;
        }

        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
        }
        messages.Add(new JObject { ["role"] = "user", ["content"] = userContent });

        var requestBody = new JObject
        {
            ["model"] = "Phi-4-multimodal-instruct",
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

    private async Task<JObject> ExecuteChatMiniToolAsync(JObject arguments)
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
            ["model"] = "Phi-4-mini-instruct",
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
            Content = new StringContent(responseObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data))
        {
            error["data"] = data;
        }

        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ========================================
    // APPLICATION INSIGHTS TELEMETRY
    // ========================================

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
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
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring("InstrumentationKey=".Length);
            }
        }
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring("IngestionEndpoint=".Length);
            }
        }
        return "https://dc.services.visualstudio.com/";
    }
}
