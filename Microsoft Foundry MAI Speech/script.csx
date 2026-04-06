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

    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private const string ServerName = "foundry-mai-speech";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";

    // ========================================
    // MAIN ENTRY POINT
    // ========================================
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        
        this.Context.Logger.LogInformation($"[{correlationId}] Request received: {this.Context.OperationId}");

        try
        {
            _ = LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId
            });

            HttpResponseMessage response;
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                    break;

                case "TranscribeAudio":
                    response = await HandleTranscribeAsync(correlationId).ConfigureAwait(false);
                    break;

                case "TranslateAudio":
                    response = await HandleTranslateAsync(correlationId).ConfigureAwait(false);
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

    private async Task<HttpResponseMessage> HandleTranscribeAsync(string correlationId)
    {
        _ = LogToAppInsights("TranscribeRequest", new
        {
            CorrelationId = correlationId,
            RequestUrl = this.Context.Request.RequestUri.ToString()
        });

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            try
            {
                var result = JObject.Parse(responseString);
                _ = LogToAppInsights("TranscribeResponse", new
                {
                    CorrelationId = correlationId,
                    Language = result["language"]?.ToString(),
                    Duration = result["duration"]?.Value<double>() ?? 0,
                    TextLength = result["text"]?.ToString()?.Length ?? 0
                });
            }
            catch { }
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("TranscribeError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
        }

        return response;
    }

    private async Task<HttpResponseMessage> HandleTranslateAsync(string correlationId)
    {
        _ = LogToAppInsights("TranslateRequest", new
        {
            CorrelationId = correlationId
        });

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("TranslateError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
        }

        return response;
    }

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
            Method = method
        });

        if (requestId == null || requestId.Type == JTokenType.Null)
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
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
                ["name"] = "transcribe_audio",
                ["description"] = "Transcribe audio to text using MAI-Transcribe-1. Best-in-class accuracy (3.9% WER), 25+ languages. Provide an audio URL for transcription.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["audio_url"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "URL of the audio file to transcribe"
                        },
                        ["deployment_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Deployment name of the speech model (default: MAI-Transcribe-1)"
                        },
                        ["language"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "ISO-639-1 language code (e.g., en, es, fr). Auto-detected if not specified."
                        },
                        ["response_format"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Output format: json, text, srt, verbose_json, or vtt"
                        }
                    },
                    ["required"] = new JArray { "audio_url" }
                }
            },
            new JObject
            {
                ["name"] = "chat_completion",
                ["description"] = "Send a chat message to an LLM. Use to summarize, analyze, or discuss transcription results — extract key topics, generate meeting minutes, identify action items, or translate the transcript.",
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
                case "transcribe_audio":
                    toolResult = await ExecuteTranscribeAudioToolAsync(arguments).ConfigureAwait(false);
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

    private async Task<JObject> ExecuteTranscribeAudioToolAsync(JObject arguments)
    {
        var audioUrl = arguments.Value<string>("audio_url");
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            throw new ArgumentException("'audio_url' is required");
        }

        var language = arguments.Value<string>("language");
        var responseFormat = arguments.Value<string>("response_format") ?? "verbose_json";
        var deploymentId = arguments.Value<string>("deployment_id") ?? "MAI-Transcribe-1";

        // Download the audio file
        var audioRequest = new HttpRequestMessage(HttpMethod.Get, audioUrl);
        var audioResponse = await this.Context.SendAsync(audioRequest, this.CancellationToken).ConfigureAwait(false);
        
        if (!audioResponse.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to download audio from URL ({(int)audioResponse.StatusCode})");
        }

        var audioBytes = await audioResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var fileName = "audio.wav";
        try
        {
            var uri = new Uri(audioUrl);
            var lastSegment = uri.Segments.LastOrDefault()?.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(lastSegment) && lastSegment.Contains("."))
            {
                fileName = lastSegment;
            }
        }
        catch { }

        // Build multipart form
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", fileName);
        multipart.Add(new StringContent(responseFormat), "response_format");

        if (!string.IsNullOrWhiteSpace(language))
        {
            multipart.Add(new StringContent(language), "language");
        }

        // Build the transcription URL
        var baseUri = this.Context.Request.RequestUri;
        var host = baseUri.Host;
        var deploymentHost = host.Replace(".services.ai.azure.com", ".openai.azure.com");
        var url = $"https://{deploymentHost}/openai/deployments/{Uri.EscapeDataString(deploymentId)}/audio/transcriptions?api-version=2024-10-21";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = multipart;
        
        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("api-key", this.Context.Request.Headers.GetValues("api-key"));
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Transcription failed ({(int)response.StatusCode}): {content}");
        }

        var result = JObject.Parse(content);

        return new JObject
        {
            ["text"] = result["text"]?.ToString(),
            ["language"] = result["language"]?.ToString(),
            ["duration"] = result["duration"]?.Value<double>() ?? 0,
            ["segments"] = result["segments"]
        };
    }

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
        
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
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
