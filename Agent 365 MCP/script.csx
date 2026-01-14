using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string ServerName = "Agent365McpProxy";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-12-01";

    private static bool _isInitialized = false;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var envId = GetConnectionParameter("envId");
        if (string.IsNullOrWhiteSpace(envId))
        {
            return CreateJsonRpcErrorResponse(null, -32602, "Invalid params", "Missing connection parameter: envId");
        }

        string body;
        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Unable to read request body");
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
        var requestId = request["id"];

        try
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize(envId, requestId);

                case "notifications/initialized":
                    return HandleInitializedNotification();

                case "tools/list":
                    return HandleToolsList(requestId);

                case "tools/call":
                    return await HandleToolsCallAsync(envId, request, requestId).ConfigureAwait(false);

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (JsonException ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(string envId, JToken requestId)
    {
        _isInitialized = true;

        var result = new JObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject
                {
                    ["listChanged"] = true
                }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleInitializedNotification()
    {
        _isInitialized = true;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"initialized\"}", Encoding.UTF8, "application/json")
        };
        return response;
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        if (!_isInitialized)
        {
            return CreateJsonRpcErrorResponse(requestId, -32002, "Server not initialized", "Call initialize first");
        }

        var result = new JObject
        {
            ["tools"] = BuildToolsList()
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(string envId, JObject request, JToken requestId)
    {
        if (!_isInitialized)
        {
            return CreateJsonRpcErrorResponse(requestId, -32002, "Server not initialized", "Call initialize first");
        }

        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject;
        var payload = arguments?["payload"];

        if (string.IsNullOrWhiteSpace(toolName) || payload == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "name and arguments.payload are required");
        }

        var serverName = ResolveServerName(toolName);
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", $"Unknown tool: {toolName}");
        }

        var targetUrl = BuildServerUrl(envId, serverName);
        var forwardRequest = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };

        var response = await this.Context.SendAsync(forwardRequest, this.CancellationToken).ConfigureAwait(false);
        var respContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var result = new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = respContent
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private string BuildServerUrl(string envId, string serverName)
    {
        return $"https://agent365.svc.cloud.microsoft/mcp/environments/{envId}/servers/{serverName}";
    }

    private string ResolveServerName(string toolName)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "admintools", "AdminTools" },
            { "searchtools", "SearchTools" },
            { "me", "Me" },
            { "dataverse", "Dataverse" },
            { "mcpmanagement", "MCPManagement" },
            { "mail", "Mail" },
            { "calendar", "Calendar" },
            { "odspremoteserver", "ODSPRemoteServer" },
            { "sharepointlisttools", "SharePointListTools" },
            { "teams", "Teams" },
            { "word", "Word" }
        };

        return map.TryGetValue(toolName ?? string.Empty, out var value) ? value : null;
    }

    private JArray BuildToolsList()
    {
        var tools = new List<JObject>
        {
            Tool("admintools", "Admin tools for Agent 365 governance (admintools reference)"),
            Tool("searchtools", "Copilot Search tools for Microsoft 365 content (searchtools reference)"),
            Tool("me", "User profile tools (manager, reports, profile info)"),
            Tool("dataverse", "Dataverse CRUD and domain tools"),
            Tool("mcpmanagement", "MCP Management server tools to create/update MCP servers and tools"),
            Tool("mail", "Outlook Mail tools (create, update, delete, reply)"),
            Tool("calendar", "Outlook Calendar tools (create/update events, accept/decline)"),
            Tool("odspremoteserver", "OneDrive/SharePoint file operations (upload, metadata, search)"),
            Tool("sharepointlisttools", "SharePoint list tools (list, create, update items)"),
            Tool("teams", "Teams tools (chat, channel, membership, messaging)"),
            Tool("word", "Word document tools (create/read, comments)")
        };

        return new JArray(tools);
    }

    private JObject Tool(string name, string description)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["payload"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "JSON-RPC payload to forward to the selected MCP server"
                    }
                },
                ["required"] = new JArray("payload")
            }
        };
    }

    private string GetConnectionParameter(string name)
    {
        try
        {
            var raw = this.Context.ConnectionParameters[name]?.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch
        {
            return null;
        }
    }

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
            Content = new StringContent(responseObj.ToString(Formatting.None), Encoding.UTF8, "application/json")
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

        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseObj.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
    }
}
