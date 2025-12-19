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
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var envId = GetConnectionParameter("envId");
        if (string.IsNullOrWhiteSpace(envId))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Missing connection parameter: envId", Encoding.UTF8, "text/plain")
            };
        }

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch
        {
            return await ProxyRawAsync(envId, body).ConfigureAwait(false);
        }

        var method = request.Value<string>("method");
        var idToken = request["id"];

        switch (method)
        {
            case "initialize":
                return CreateMcpResponse(idToken, new JObject
                {
                    ["capabilities"] = new JObject
                    {
                        ["streaming"] = false
                    }
                });

            case "tools/list":
                return CreateMcpResponse(idToken, new JObject
                {
                    ["tools"] = BuildToolsList()
                });

            case "tools/call":
                return await HandleToolsCallAsync(envId, request, idToken).ConfigureAwait(false);

            default:
                // Fallback: proxy to MCPManagement unchanged
                return await ProxyRawAsync(envId, body).ConfigureAwait(false);
        }
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

    private async Task<HttpResponseMessage> HandleToolsCallAsync(string envId, JObject request, JToken idToken)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject;
        var payload = arguments?["payload"];

        if (string.IsNullOrWhiteSpace(toolName) || payload == null)
        {
            return CreateMcpError(idToken, -32602, "Invalid params: name and arguments.payload are required");
        }

        var serverName = ResolveServerName(toolName);
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return CreateMcpError(idToken, -32601, $"Unknown tool: {toolName}");
        }

        var targetUrl = BuildServerUrl(envId, serverName);
        var forwardRequest = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };

        var response = await this.Context.SendAsync(forwardRequest, this.CancellationToken).ConfigureAwait(false);
        var respContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return CreateMcpResponse(idToken, new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = respContent
                }
            }
        });
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
            Tool("admintools", "Admin tools for Agent 365 governance (see admintools reference)"),
            Tool("searchtools", "Copilot Search tools for Microsoft 365 content (see searchtools reference)"),
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

    private HttpResponseMessage CreateMcpResponse(JToken idToken, JObject result)
    {
        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result
        };

        if (idToken != null)
        {
            responseObj["id"] = idToken;
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseObj.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateMcpError(JToken idToken, int code, string message)
    {
        var errorObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        if (idToken != null)
        {
            errorObj["id"] = idToken;
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(errorObj.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private async Task<HttpResponseMessage> ProxyRawAsync(string envId, string body)
    {
        var serverUrl = BuildServerUrl(envId, "MCPManagement");
        var forwardRequest = new HttpRequestMessage(HttpMethod.Post, serverUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await this.Context.SendAsync(forwardRequest, this.CancellationToken).ConfigureAwait(false);
        var respContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(respContent, Encoding.UTF8, "application/json")
        };
    }
}
