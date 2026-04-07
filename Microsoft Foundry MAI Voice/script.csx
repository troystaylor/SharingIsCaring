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
    private const string ServerName = "foundry-mai-voice";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            case "InvokeMCP":
                return await HandleMcpRequestAsync().ConfigureAwait(false);

            default:
                return await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
                    .ConfigureAwait(false);
        }
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
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
                return await HandleMcpToolsCallAsync(request, requestId).ConfigureAwait(false);

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
        return CreateJsonRpcSuccessResponse(requestId, new JObject
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
        });
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
                ["name"] = "synthesize_speech",
                ["description"] = "Convert text to natural-sounding speech audio using MAI-Voice-1 or other TTS voices. Provide text and a voice name to generate audio. Returns an audio URL or confirms synthesis.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["text"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The text to convert to speech"
                        },
                        ["voice"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Voice short name (e.g., en-US-JennyNeural). Use list_voices to find available voices."
                        },
                        ["language"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Language code (e.g., en-US). Defaults to en-US."
                        },
                        ["output_format"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Audio format: audio-24khz-96kbitrate-mono-mp3 (default), audio-48khz-192kbitrate-mono-mp3, riff-24khz-16bit-mono-pcm, ogg-24khz-16bit-mono-opus"
                        }
                    },
                    ["required"] = new JArray { "text" }
                }
            },
            new JObject
            {
                ["name"] = "list_voices",
                ["description"] = "Get all available TTS voices for the configured region. Returns voice names, locales, genders, and supported speaking styles. Use this to find the right voice for synthesize_speech.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["locale_filter"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional locale filter (e.g., en-US, fr-FR). Returns all voices if omitted."
                        }
                    }
                }
            },
            new JObject
            {
                ["name"] = "chat_completion",
                ["description"] = "Send a chat message to an LLM. Use to generate SSML markup, suggest voice selections, write scripts for narration, or discuss speech synthesis parameters.",
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

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        try
        {
            JObject toolResult;

            switch (toolName.ToLowerInvariant())
            {
                case "synthesize_speech":
                    toolResult = await ExecuteSynthesizeSpeechToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "list_voices":
                    toolResult = await ExecuteListVoicesToolAsync(arguments).ConfigureAwait(false);
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

    private async Task<JObject> ExecuteSynthesizeSpeechToolAsync(JObject arguments)
    {
        var text = arguments.Value<string>("text");
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("'text' is required");
        }

        var voice = arguments.Value<string>("voice") ?? "en-US-JennyNeural";
        var language = arguments.Value<string>("language") ?? "en-US";
        var outputFormat = arguments.Value<string>("output_format") ?? "audio-24khz-96kbitrate-mono-mp3";

        var ssml = $@"<speak version='1.0' xml:lang='{language}'>
    <voice xml:lang='{language}' name='{voice}'>{System.Security.SecurityElement.Escape(text)}</voice>
</speak>";

        var baseUri = this.Context.Request.RequestUri;
        var host = baseUri.Host;
        // MCP comes in on .services.ai.azure.com, need to route to .tts.speech.microsoft.com
        // Extract region from resourceName or use a default
        var ttsHost = host.Replace(".services.ai.azure.com", "");
        var url = $"https://{ttsHost}.tts.speech.microsoft.com/cognitiveservices/v1";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");
        request.Headers.Add("X-Microsoft-OutputFormat", outputFormat);
        request.Headers.Add("User-Agent", "FoundryMAIVoice");

        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", 
                this.Context.Request.Headers.GetValues("api-key").FirstOrDefault());
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new Exception($"Speech synthesis failed ({(int)response.StatusCode}): {errorContent}");
        }

        var audioBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        return new JObject
        {
            ["status"] = "success",
            ["voice"] = voice,
            ["language"] = language,
            ["output_format"] = outputFormat,
            ["audio_size_bytes"] = audioBytes.Length,
            ["text_length"] = text.Length,
            ["message"] = $"Generated {audioBytes.Length} bytes of audio using voice '{voice}'. Use the SynthesizeSpeech REST operation to retrieve the audio file directly."
        };
    }

    private async Task<JObject> ExecuteListVoicesToolAsync(JObject arguments)
    {
        var localeFilter = arguments.Value<string>("locale_filter");

        var baseUri = this.Context.Request.RequestUri;
        var host = baseUri.Host;
        var ttsHost = host.Replace(".services.ai.azure.com", "");
        var url = $"https://{ttsHost}.tts.speech.microsoft.com/cognitiveservices/voices/list";

        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key",
                this.Context.Request.Headers.GetValues("api-key").FirstOrDefault());
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to list voices ({(int)response.StatusCode}): {content}");
        }

        var voices = JArray.Parse(content);

        // Filter by locale if specified
        if (!string.IsNullOrWhiteSpace(localeFilter))
        {
            voices = new JArray(voices.Where(v => 
                v["Locale"]?.ToString().StartsWith(localeFilter, StringComparison.OrdinalIgnoreCase) == true));
        }

        // Return summary to avoid massive response
        var summary = new JArray(voices.Select(v => new JObject
        {
            ["shortName"] = v["ShortName"],
            ["displayName"] = v["DisplayName"],
            ["gender"] = v["Gender"],
            ["locale"] = v["Locale"],
            ["styles"] = v["StyleList"]
        }));

        return new JObject
        {
            ["voice_count"] = summary.Count,
            ["voices"] = summary
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

        var baseUri = this.Context.Request.RequestUri;
        var url = $"https://{baseUri.Host}/models/chat/completions";

        var apiRequest = new HttpRequestMessage(HttpMethod.Post, url);
        apiRequest.Content = new StringContent(requestBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        if (this.Context.Request.Headers.Authorization != null)
        {
            apiRequest.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        if (this.Context.Request.Headers.Contains("api-key"))
        {
            apiRequest.Headers.Add("api-key", this.Context.Request.Headers.GetValues("api-key"));
        }

        apiRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(apiRequest, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Chat completion failed ({(int)response.StatusCode}): {content}");
        }

        var result = JObject.Parse(content);
        var responseContent = result["choices"]?[0]?["message"]?["content"]?.ToString();

        return new JObject
        {
            ["response"] = responseContent ?? "No response generated",
            ["usage"] = result["usage"]
        };
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
}
