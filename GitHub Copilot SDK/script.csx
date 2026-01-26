using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// GitHub Copilot SDK Proxy MCP Server
/// Proxies JSON-RPC requests over TCP to a running Copilot CLI server (copilot server --port <port>).
/// Exposes tools to create/resume sessions, send messages, list/delete sessions, ping, list models, and get status.
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // CONFIGURATION
    // ========================================
    /// <summary>
    /// Default URL for the Copilot SDK JSON-RPC proxy
    /// Update this to your Cloudflare Tunnel URL when it changes
    /// </summary>
    private const string DefaultSdkUrl = "[REPLACE_WITH_CLOUDFLARE_HOST]/jsonrpc";

    /// <summary>
    /// Application Insights connection string
    /// Format: InstrumentationKey=YOUR-KEY-HERE;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;LiveEndpoint=https://REGION.livediagnostics.monitor.azure.com/
    /// Get from: Azure Portal → Application Insights resource → Overview → Connection String
    /// Leave empty to disable telemetry
    /// </summary>
    private const string AppInsightsConnectionString = "";

    private const string ServerName = "github-copilot-sdk";
    private const string ServerVersion = "1.0.0";
    private const string ServerTitle = "GitHub Copilot SDK";
    private const string ServerDescription = "Proxy to GitHub Copilot SDK via JSON-RPC";
    private const string ProtocolVersion = "2025-11-25";
    private const string ServerInstructions = @"Use these tools to interact with GitHub Copilot SDK via HTTP JSON-RPC.
Create sessions with optional model selection, send prompts, and manage session lifecycle.
Supported methods: session.create, session.send, session.resume, session.list, session.delete, ping, status.get, auth.status, models.list.";

    private const string TOOL_CREATE_SESSION = "copilot_create_session";
    private const string TOOL_SEND = "copilot_send";
    private const string TOOL_RESUME_SESSION = "copilot_resume_session";
    private const string TOOL_LIST_SESSIONS = "copilot_list_sessions";
    private const string TOOL_DELETE_SESSION = "copilot_delete_session";
    private const string TOOL_PING = "copilot_ping";
    private const string TOOL_LIST_MODELS = "copilot_list_models";
    private const string TOOL_GET_STATUS = "copilot_get_status";
    private const string TOOL_GET_AUTH_STATUS = "copilot_get_auth_status";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        string body = null;
        JObject request = null;
        string method = null;
        JToken requestId = null;
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            await LogEventAsync("RequestReceived", new
            {
                CorrelationId = correlationId,
                Path = this.Context?.Request?.RequestUri?.AbsolutePath,
                Method = this.Context?.Request?.Method?.Method
            }).ConfigureAwait(false);

            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
            }

            request = JObject.Parse(body);
            method = request.Value<string>("method") ?? string.Empty;
            requestId = request["id"];

            switch (method)
            {
                case "initialize":
                    return CreateJsonRpcSuccessResponse(requestId, BuildInitializeResult(request));
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());
                case "tools/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = BuildToolsList() });
                case "tools/call":
                    return await HandleToolsCallAsync(request, requestId, correlationId).ConfigureAwait(false);
                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (JsonException ex)
        {
            await LogEventAsync("RequestError", new { CorrelationId = correlationId, ErrorMessage = ex.Message, ErrorType = ex.GetType().Name }).ConfigureAwait(false);
            return CreateJsonRpcErrorResponse(requestId, -32700, "Parse error", "Invalid JSON");
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] Internal error: {ex.Message}");
            await LogEventAsync("RequestError", new { CorrelationId = correlationId, ErrorMessage = ex.Message, ErrorType = ex.GetType().Name }).ConfigureAwait(false);
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
        finally
        {
            await LogEventAsync("RequestCompleted", new { CorrelationId = correlationId, DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds }).ConfigureAwait(false);
        }
    }

    private JObject BuildInitializeResult(JObject request)
    {
        var clientProtocolVersion = request["params"]? ["protocolVersion"]?.ToString() ?? ProtocolVersion;
        return new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = ServerTitle,
                ["description"] = ServerDescription
            },
            ["instructions"] = ServerInstructions
        };
    }

    private JArray BuildToolsList()
    {
        return new JArray
        {
            BuildTool(TOOL_CREATE_SESSION, "Create a Copilot session (session.create)", new JObject
            {
                ["model"] = new JObject { ["type"] = "string" },
                ["sessionId"] = new JObject { ["type"] = "string" },
                ["tools"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } },
                ["systemMessage"] = new JObject { ["type"] = "string" },
                ["availableTools"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                ["excludedTools"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                ["provider"] = new JObject { ["type"] = "object" },
                ["streaming"] = new JObject { ["type"] = "boolean" },
                ["mcpServers"] = new JObject { ["type"] = "object" },
                ["customAgents"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } },
                ["configDir"] = new JObject { ["type"] = "string" },
                ["skillDirectories"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                ["disabledSkills"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                ["cliHost"] = new JObject { ["type"] = "string" },
                ["cliPort"] = new JObject { ["type"] = "integer" },
                ["cliUrl"] = new JObject { ["type"] = "string", ["description"] = "HTTP URL of proxy or CLI server (/jsonrpc)" }
            }, new JArray()),
            BuildTool(TOOL_SEND, "Send a prompt to a session (session.send)", new JObject
            {
                ["sessionId"] = new JObject { ["type"] = "string" },
                ["prompt"] = new JObject { ["type"] = "string" },
                ["attachments"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } },
                ["mode"] = new JObject { ["type"] = "string" },
                ["cliHost"] = new JObject { ["type"] = "string" },
                ["cliPort"] = new JObject { ["type"] = "integer" },
                ["cliUrl"] = new JObject { ["type"] = "string" }
            }, new JArray { "sessionId", "prompt" }),
            BuildTool(TOOL_RESUME_SESSION, "Resume a session (session.resume)", new JObject
            {
                ["sessionId"] = new JObject { ["type"] = "string" },
                ["tools"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } },
                ["provider"] = new JObject { ["type"] = "object" },
                ["streaming"] = new JObject { ["type"] = "boolean" },
                ["mcpServers"] = new JObject { ["type"] = "object" },
                ["customAgents"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } },
                ["skillDirectories"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                ["disabledSkills"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                ["cliHost"] = new JObject { ["type"] = "string" },
                ["cliPort"] = new JObject { ["type"] = "integer" },
                ["cliUrl"] = new JObject { ["type"] = "string" }
            }, new JArray { "sessionId" }),
            BuildTool(TOOL_LIST_SESSIONS, "List sessions (session.list)", new JObject
            {
                ["cliHost"] = new JObject { ["type"] = "string" },
                ["cliPort"] = new JObject { ["type"] = "integer" },
                ["cliUrl"] = new JObject { ["type"] = "string" }
            }, new JArray()),
            BuildTool(TOOL_DELETE_SESSION, "Delete a session (session.delete)", new JObject
            {
                ["sessionId"] = new JObject { ["type"] = "string" },
                ["cliHost"] = new JObject { ["type"] = "string" },
                ["cliPort"] = new JObject { ["type"] = "integer" },
                ["cliUrl"] = new JObject { ["type"] = "string" }
            }, new JArray { "sessionId" }),
            BuildTool(TOOL_PING, "Ping server (ping)", new JObject
            {
                ["message"] = new JObject { ["type"] = "string" },
                ["cliHost"] = new JObject { ["type"] = "string" },
                ["cliPort"] = new JObject { ["type"] = "integer" },
                ["cliUrl"] = new JObject { ["type"] = "string" }
            }, new JArray()),
            BuildTool(TOOL_LIST_MODELS, "List models (models.list)", new JObject
            {
                ["cliHost"] = new JObject { ["type"] = "string" },
                ["cliPort"] = new JObject { ["type"] = "integer" },
                ["cliUrl"] = new JObject { ["type"] = "string" }
            }, new JArray()),
            BuildTool(TOOL_GET_STATUS, "Get status (status.get)", new JObject
            {
                ["cliHost"] = new JObject { ["type"] = "string" },
                ["cliPort"] = new JObject { ["type"] = "integer" },
                ["cliUrl"] = new JObject { ["type"] = "string" }
            }, new JArray()),
            BuildTool(TOOL_GET_AUTH_STATUS, "Get auth status (auth.status)", new JObject
            {
                ["cliHost"] = new JObject { ["type"] = "string" },
                ["cliPort"] = new JObject { ["type"] = "integer" },
                ["cliUrl"] = new JObject { ["type"] = "string" }
            }, new JArray())
        };
    }

    private JObject BuildTool(string name, string description, JObject properties, JArray required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            },
            ["annotations"] = new JObject { ["readOnlyHint"] = true, ["idempotentHint"] = true }
        };
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject request, JToken requestId, string correlationId)
    {
        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();
        var toolStart = DateTime.UtcNow;

        await LogEventAsync("ToolCall", new { CorrelationId = correlationId, Tool = toolName }).ConfigureAwait(false);

        try
        {
            JObject result = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);
            await LogEventAsync("ToolCompleted", new { CorrelationId = correlationId, Tool = toolName, DurationMs = (DateTime.UtcNow - toolStart).TotalMilliseconds }).ConfigureAwait(false);
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented) } },
                ["isError"] = false
            });
        }
        catch (ArgumentException ex)
        {
            await LogEventAsync("ToolError", new { CorrelationId = correlationId, Tool = toolName, ErrorMessage = ex.Message, ErrorType = ex.GetType().Name }).ConfigureAwait(false);
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            await LogEventAsync("ToolError", new { CorrelationId = correlationId, Tool = toolName, ErrorMessage = ex.Message, ErrorType = ex.GetType().Name }).ConfigureAwait(false);
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject args)
    {
        var transport = ResolveTransport(args);
        switch (toolName)
        {
            case TOOL_CREATE_SESSION:
                return await SendJsonRpcAsync(transport, "session.create", BuildCreateSessionPayload(args)).ConfigureAwait(false);
            case TOOL_SEND:
                return await SendJsonRpcAsync(transport, "session.send", new JObject
                {
                    ["sessionId"] = RequireArg(args, "sessionId"),
                    ["prompt"] = RequireArg(args, "prompt"),
                    ["attachments"] = args["attachments"],
                    ["mode"] = args["mode"]
                }).ConfigureAwait(false);
            case TOOL_RESUME_SESSION:
                return await SendJsonRpcAsync(transport, "session.resume", BuildResumePayload(args)).ConfigureAwait(false);
            case TOOL_LIST_SESSIONS:
                return await SendJsonRpcAsync(transport, "session.list", new JObject()).ConfigureAwait(false);
            case TOOL_DELETE_SESSION:
                return await SendJsonRpcAsync(transport, "session.delete", new JObject { ["sessionId"] = RequireArg(args, "sessionId") }).ConfigureAwait(false);
            case TOOL_PING:
                return await SendJsonRpcAsync(transport, "ping", new JObject { ["message"] = args["message"] }).ConfigureAwait(false);
            case TOOL_LIST_MODELS:
                return await SendJsonRpcAsync(transport, "models.list", new JObject()).ConfigureAwait(false);
            case TOOL_GET_STATUS:
                return await SendJsonRpcAsync(transport, "status.get", new JObject()).ConfigureAwait(false);
            case TOOL_GET_AUTH_STATUS:
                return await SendJsonRpcAsync(transport, "auth.status", new JObject()).ConfigureAwait(false);
            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private JObject BuildCreateSessionPayload(JObject args)
    {
        var payload = new JObject();
        if (args["model"] != null) payload["model"] = args["model"];
        if (args["sessionId"] != null) payload["sessionId"] = args["sessionId"];
        if (args["tools"] != null) payload["tools"] = args["tools"];
        if (args["systemMessage"] != null) payload["systemMessage"] = args["systemMessage"];
        if (args["availableTools"] != null) payload["availableTools"] = args["availableTools"];
        if (args["excludedTools"] != null) payload["excludedTools"] = args["excludedTools"];
        if (args["provider"] != null) payload["provider"] = args["provider"];
        if (args["streaming"] != null) payload["streaming"] = args["streaming"];
        if (args["mcpServers"] != null) payload["mcpServers"] = args["mcpServers"];
        if (args["customAgents"] != null) payload["customAgents"] = args["customAgents"];
        if (args["configDir"] != null) payload["configDir"] = args["configDir"];
        if (args["skillDirectories"] != null) payload["skillDirectories"] = args["skillDirectories"];
        if (args["disabledSkills"] != null) payload["disabledSkills"] = args["disabledSkills"];
        return payload;
    }

    private JObject BuildResumePayload(JObject args)
    {
        var payload = new JObject
        {
            ["sessionId"] = RequireArg(args, "sessionId")
        };
        if (args["tools"] != null) payload["tools"] = args["tools"];
        if (args["provider"] != null) payload["provider"] = args["provider"];
        if (args["streaming"] != null) payload["streaming"] = args["streaming"];
        if (args["mcpServers"] != null) payload["mcpServers"] = args["mcpServers"];
        if (args["customAgents"] != null) payload["customAgents"] = args["customAgents"];
        if (args["skillDirectories"] != null) payload["skillDirectories"] = args["skillDirectories"];
        if (args["disabledSkills"] != null) payload["disabledSkills"] = args["disabledSkills"];
        return payload;
    }

    private async Task<JObject> SendJsonRpcAsync(Transport transport, string method, JObject payload)
    {
        var requestObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = method,
            ["params"] = payload ?? new JObject()
        };
        var requestBody = requestObj.ToString(Newtonsoft.Json.Formatting.None);

        if (transport.Type == TransportType.Http && !string.IsNullOrWhiteSpace(transport.Url))
        {
            return await SendJsonRpcHttpAsync(transport.Url, requestBody).ConfigureAwait(false);
        }
        return await SendJsonRpcTcpAsync(transport.Host, transport.Port, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> SendJsonRpcHttpAsync(string url, string requestBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var bodyText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {response.StatusCode}: {bodyText}");
        }
        var json = JObject.Parse(bodyText);
        if (json["error"] != null)
        {
            throw new InvalidOperationException(json["error"].ToString());
        }
        return json["result"] as JObject ?? json;
    }

    private async Task<JObject> SendJsonRpcTcpAsync(string host, int port, string requestBody)
    {
        var requestBytes = Encoding.UTF8.GetBytes(requestBody);
        var header = $"Content-Length: {requestBytes.Length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        using (var client = new TcpClient())
        {
            client.ReceiveTimeout = 15000;
            client.SendTimeout = 15000;
            await client.ConnectAsync(host, port).ConfigureAwait(false);
            using (var stream = client.GetStream())
            {
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
                await stream.WriteAsync(requestBytes, 0, requestBytes.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);

                using (var ms = new MemoryStream())
                {
                    var buf = new byte[1];
                    bool found = false;
                    while (!found)
                    {
                        int read = await stream.ReadAsync(buf, 0, 1).ConfigureAwait(false);
                        if (read == 0) break;
                        ms.WriteByte(buf[0]);
                        var text = Encoding.ASCII.GetString(ms.ToArray());
                        if (text.Contains("\r\n\r\n"))
                        {
                            found = true;
                            break;
                        }
                    }
                    var headerText = Encoding.ASCII.GetString(ms.ToArray());
                    var contentLength = ParseContentLength(headerText);
                    if (contentLength <= 0)
                    {
                        throw new InvalidOperationException($"Invalid Content-Length in response: {headerText}");
                    }
                    var bodyBuf = new byte[contentLength];
                    int offset = 0;
                    while (offset < contentLength)
                    {
                        int read = await stream.ReadAsync(bodyBuf, offset, contentLength - offset).ConfigureAwait(false);
                        if (read == 0) break;
                        offset += read;
                    }
                    var bodyText = Encoding.UTF8.GetString(bodyBuf, 0, offset);
                    var json = JObject.Parse(bodyText);
                    if (json["error"] != null)
                    {
                        throw new InvalidOperationException(json["error"].ToString());
                    }
                    return json["result"] as JObject ?? json;
                }
            }
        }
    }

    private int ParseContentLength(string headerText)
    {
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int len))
                {
                    return len;
                }
            }
        }
        return -1;
    }

    private async Task LogEventAsync(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(AppInsightsConnectionString);
            var ingestionEndpoint = ExtractIngestionEndpoint(AppInsightsConnectionString);
            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint)) return;

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? string.Empty;
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

            var json = JsonConvert.SerializeObject(telemetryData);
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore telemetry errors
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        if (!connectionString.Contains("="))
        {
            return connectionString; // treat as raw instrumentation key
        }
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

    private enum TransportType { Tcp, Http }
    private class Transport { public TransportType Type { get; set; } public string Host { get; set; } public int Port { get; set; } public string Url { get; set; } }

    private Transport ResolveTransport(JObject args)
    {
        // Priority: connection parameter (via header) > tool argument > hardcoded default
        string cliUrl = null;

        // 1. Check for URL from connection parameter (set via policyTemplate header)
        if (this.Context?.Request?.Headers != null)
        {
            if (this.Context.Request.Headers.TryGetValues("x-copilot-proxy-url", out var headerValues))
            {
                cliUrl = headerValues.FirstOrDefault();
            }
        }

        // 2. Fall back to tool argument
        if (string.IsNullOrWhiteSpace(cliUrl))
        {
            cliUrl = args["cliUrl"]?.ToString();
        }

        // 3. Fall back to hardcoded default
        if (string.IsNullOrWhiteSpace(cliUrl))
        {
            cliUrl = DefaultSdkUrl;
        }

        if (!string.IsNullOrWhiteSpace(cliUrl))
        {
            // Default path if not provided
            if (!cliUrl.Contains("/"))
            {
                // Assume host:port; build http://host:port/jsonrpc
                cliUrl = $"http://{cliUrl}/jsonrpc";
            }
            else if (cliUrl.EndsWith("/"))
            {
                cliUrl = cliUrl + "jsonrpc";
            }
            return new Transport { Type = TransportType.Http, Url = cliUrl };
        }

        // Fallback to TCP if no URL configured
        var host = args["cliHost"]?.ToString() ?? "localhost";
        var portStr = args["cliPort"]?.ToString();
        if (!int.TryParse(portStr, out int port) || port <= 0)
        {
            throw new ArgumentException("Copilot SDK URL not configured. Set via connection parameter or tool argument.");
        }
        return new Transport { Type = TransportType.Tcp, Host = host, Port = port };
    }

    private string RequireArg(JObject args, string name)
    {
        var value = args[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{name}' is required");
        }
        return value;
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
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
        if (!string.IsNullOrWhiteSpace(data))
        {
            error["data"] = data;
        }
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }
}
