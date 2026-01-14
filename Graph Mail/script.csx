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
    private static readonly string SERVER_NAME = "outlook-mail-mcp";
    private static readonly string SERVER_VERSION = "1.0.0";
    private static readonly string DEFAULT_PROTOCOL_VERSION = "2025-12-01";

    private static bool _isInitialized = false;

    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "listMessages",
            ["description"] = "List messages for a user using Microsoft Graph. Based strictly on GET /users/{userId}/messages.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["userId"] = new JObject { ["type"] = "string", ["description"] = "User ID or 'me'" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "$top query option (1-1000)" },
                    ["filter"] = new JObject { ["type"] = "string", ["description"] = "$filter OData expression" },
                    ["orderby"] = new JObject { ["type"] = "string", ["description"] = "$orderby fields (e.g., receivedDateTime desc)" },
                    ["select"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated list for $select (e.g., subject,from,receivedDateTime)" },
                    ["previewOnly"] = new JObject { ["type"] = "boolean", ["description"] = "If true, return only bodyPreview and omit body to reduce tokens" }
                },
                ["required"] = new JArray { "userId" }
            }
        },
        new JObject
        {
            ["name"] = "getMessage",
            ["description"] = "Get a specific message. Based strictly on GET /users/{userId}/messages/{messageId}.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["userId"] = new JObject { ["type"] = "string", ["description"] = "User ID or 'me'" },
                    ["messageId"] = new JObject { ["type"] = "string", ["description"] = "Message ID" },
                    ["select"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated list for $select (e.g., subject,from,receivedDateTime)" },
                    ["previewOnly"] = new JObject { ["type"] = "boolean", ["description"] = "If true, return only bodyPreview and omit body to reduce tokens" }
                },
                ["required"] = new JArray { "userId", "messageId" }
            }
        },
        new JObject
        {
            ["name"] = "createMessage",
            ["description"] = "Create a draft message using Microsoft Graph. Based strictly on POST /users/{userId}/messages.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["userId"] = new JObject { ["type"] = "string", ["description"] = "User ID or 'me'" },
                    ["message"] = new JObject { ["type"] = "object", ["description"] = "Microsoft Graph message object (subject, body, toRecipients, etc.)" }
                },
                ["required"] = new JArray { "userId", "message" }
            }
        },
        new JObject
        {
            ["name"] = "sendMail",
            ["description"] = "Send mail via Microsoft Graph action. Based strictly on POST /users/{userId}/sendMail.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["userId"] = new JObject { ["type"] = "string", ["description"] = "User ID or 'me'" },
                    ["message"] = new JObject { ["type"] = "object", ["description"] = "Microsoft Graph message object (subject, body, toRecipients, etc.)" },
                    ["saveToSentItems"] = new JObject { ["type"] = "boolean", ["description"] = "Save to Sent Items (default true)" }
                },
                ["required"] = new JArray { "userId", "message" }
            }
        },
        new JObject
        {
            ["name"] = "replyMessage",
            ["description"] = "Reply to a message. Based strictly on POST /users/{userId}/messages/{messageId}/reply.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["userId"] = new JObject { ["type"] = "string", ["description"] = "User ID or 'me'" },
                    ["messageId"] = new JObject { ["type"] = "string", ["description"] = "Message ID" },
                    ["comment"] = new JObject { ["type"] = "string", ["description"] = "Reply comment text" }
                },
                ["required"] = new JArray { "userId", "messageId", "comment" }
            }
        }
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var request = JObject.Parse(body);
            if (!request.ContainsKey("jsonrpc")) request["jsonrpc"] = "2.0";

            var method = request["method"]?.ToString();
            var id = request["id"];
            var @params = request["params"] as JObject;

            switch (method)
            {
                case "initialize":
                    return HandleInitialize(@params, id);
                case "initialized":
                case "notifications/cancelled":
                    return CreateSuccess(new JObject(), id);
                case "tools/list":
                    return HandleToolsList(id);
                case "tools/call":
                    return await HandleToolsCallAsync(@params, id).ConfigureAwait(false);
                default:
                    return CreateError(id, -32601, "Method not found", method ?? "");
            }
        }
        catch (JsonException ex)
        {
            return CreateError(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            return CreateError(null, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(JObject @params, JToken id)
    {
        _isInitialized = true;
        var protocolVersion = @params?[("protocolVersion")]?.ToString() ?? DEFAULT_PROTOCOL_VERSION;
        var result = new JObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION,
                ["title"] = "Outlook Mail MCP",
                ["description"] = "Model Context Protocol tools for Outlook Mail via Microsoft Graph"
            }
        };
        return CreateSuccess(result, id);
    }

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        if (!_isInitialized)
            return CreateError(id, -32002, "Server not initialized", "Call initialize first");

        return CreateSuccess(new JObject { ["tools"] = AVAILABLE_TOOLS }, id);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject @params, JToken id)
    {
        if (!_isInitialized)
            return CreateError(id, -32002, "Server not initialized", "Call initialize first");

        var toolName = @params?["name"]?.ToString();
        var args = @params?["arguments"] as JObject ?? new JObject();
        if (string.IsNullOrWhiteSpace(toolName))
            return CreateError(id, -32602, "Tool name required", "name parameter is required");

        try
        {
            switch (toolName)
            {
                case "listMessages":
                    return await ExecuteListMessagesAsync(args, id).ConfigureAwait(false);
                case "getMessage":
                    return await ExecuteGetMessageAsync(args, id).ConfigureAwait(false);
                case "createMessage":
                    return await ExecuteCreateMessageAsync(args, id).ConfigureAwait(false);
                case "sendMail":
                    return await ExecuteSendMailAsync(args, id).ConfigureAwait(false);
                case "replyMessage":
                    return await ExecuteReplyMessageAsync(args, id).ConfigureAwait(false);
                default:
                    return CreateError(id, -32601, "Unknown tool", toolName);
            }
        }
        catch (ArgumentException ex)
        {
            return CreateSuccess(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = ex.Message } },
                ["isError"] = true
            }, id);
        }
        catch (Exception ex)
        {
            return CreateSuccess(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
    }

    private string GraphVersion()
    {
        try
        {
            var useBetaRaw = this.Context.ConnectionParameters["useBeta"]?.ToString();
            if (bool.TryParse(useBetaRaw, out var useBeta) && useBeta) return "beta";
        }
        catch { }
        return "v1.0";
    }

    private async Task<HttpResponseMessage> ExecuteListMessagesAsync(JObject args, JToken id)
    {
        var userId = args?["userId"]?.ToString();
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId is required");

        var top = args?["top"]?.ToString();
        var filter = args?["filter"]?.ToString();
        var orderby = args?["orderby"]?.ToString();
        var select = args?["select"]?.ToString();
        var previewOnly = args?["previewOnly"]?.ToObject<bool?>() ?? true;

        var v = GraphVersion();
        var url = new StringBuilder($"https://graph.microsoft.com/{v}/users/{Uri.EscapeDataString(userId)}/messages");
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(top)) q.Add("$top=" + Uri.EscapeDataString(top));
        if (!string.IsNullOrWhiteSpace(filter)) q.Add("$filter=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderby)) q.Add("$orderby=" + Uri.EscapeDataString(orderby));
        if (string.IsNullOrWhiteSpace(select))
        {
            select = previewOnly ? "subject,from,receivedDateTime,bodyPreview" : "subject,from,receivedDateTime,body";
        }
        if (!string.IsNullOrWhiteSpace(select)) q.Add("$select=" + Uri.EscapeDataString(select));
        if (q.Count > 0) url.Append("?" + string.Join("&", q));

        var request = new HttpRequestMessage(HttpMethod.Get, url.ToString());
        request.Headers.Add("Accept", "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return CreateSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = content } }
        }, id);
    }

    private async Task<HttpResponseMessage> ExecuteGetMessageAsync(JObject args, JToken id)
    {
        var userId = args?["userId"]?.ToString();
        var messageId = args?["messageId"]?.ToString();
        var select = args?["select"]?.ToString();
        var previewOnly = args?["previewOnly"]?.ToObject<bool?>() ?? true;
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId is required");
        if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentException("messageId is required");

        var v = GraphVersion();
        var url = new StringBuilder($"https://graph.microsoft.com/{v}/users/{Uri.EscapeDataString(userId)}/messages/{Uri.EscapeDataString(messageId)}");
        var q = new List<string>();
        if (string.IsNullOrWhiteSpace(select))
        {
            select = previewOnly ? "subject,from,receivedDateTime,bodyPreview" : "subject,from,receivedDateTime,body";
        }
        if (!string.IsNullOrWhiteSpace(select)) q.Add("$select=" + Uri.EscapeDataString(select));
        if (q.Count > 0) url.Append("?" + string.Join("&", q));

        var request = new HttpRequestMessage(HttpMethod.Get, url.ToString());
        request.Headers.Add("Accept", "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return CreateSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = content } }
        }, id);
    }

    private async Task<HttpResponseMessage> ExecuteCreateMessageAsync(JObject args, JToken id)
    {
        var userId = args?["userId"]?.ToString();
        var message = args?["message"] as JObject;
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId is required");
        if (message == null) throw new ArgumentException("message object is required");

        var v = GraphVersion();
        var url = $"https://graph.microsoft.com/{v}/users/{Uri.EscapeDataString(userId)}/messages";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(message.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Accept", "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return CreateSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = content } }
        }, id);
    }

    private async Task<HttpResponseMessage> ExecuteSendMailAsync(JObject args, JToken id)
    {
        var userId = args?["userId"]?.ToString();
        var message = args?["message"] as JObject;
        var saveToSent = args?["saveToSentItems"]?.ToObject<bool?>() ?? true;
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId is required");
        if (message == null) throw new ArgumentException("message object is required");

        var payload = new JObject {
            ["message"] = message,
            ["saveToSentItems"] = saveToSent
        };

        var v = GraphVersion();
        var url = $"https://graph.microsoft.com/{v}/users/{Uri.EscapeDataString(userId)}/sendMail";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Accept", "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // sendMail returns 202 Accepted with empty body on success
        var textOut = string.IsNullOrEmpty(content) ? $"Status: {(int)response.StatusCode} {response.StatusCode}" : content;

        return CreateSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = textOut } }
        }, id);
    }

    private async Task<HttpResponseMessage> ExecuteReplyMessageAsync(JObject args, JToken id)
    {
        var userId = args?["userId"]?.ToString();
        var messageId = args?["messageId"]?.ToString();
        var comment = args?["comment"]?.ToString();
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId is required");
        if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentException("messageId is required");
        if (string.IsNullOrWhiteSpace(comment)) throw new ArgumentException("comment is required");

        var payload = new JObject { ["comment"] = comment };

        var v = GraphVersion();
        var url = $"https://graph.microsoft.com/{v}/users/{Uri.EscapeDataString(userId)}/messages/{Uri.EscapeDataString(messageId)}/reply";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Accept", "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var textOut = string.IsNullOrEmpty(content) ? $"Status: {(int)response.StatusCode} {response.StatusCode}" : content;

        return CreateSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = textOut } }
        }, id);
    }

    private HttpResponseMessage CreateSuccess(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Content = new StringContent(json.ToString(Formatting.None), Encoding.UTF8, "application/json");
        return resp;
    }

    private HttpResponseMessage CreateError(JToken id, int code, string message, string data = null)
    {
        var err = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrEmpty(data)) err["data"] = data;
        var json = new JObject { ["jsonrpc"] = "2.0", ["error"] = err, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Content = new StringContent(json.ToString(Formatting.None), Encoding.UTF8, "application/json");
        return resp;
    }
}
