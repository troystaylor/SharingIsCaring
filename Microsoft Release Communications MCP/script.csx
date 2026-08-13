using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private const string ConnectorName = "Microsoft Release Communications MCP";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var start = DateTime.UtcNow;

        try
        {
            if (!string.Equals(this.Context.OperationId, "InvokeMCP", StringComparison.OrdinalIgnoreCase))
            {
                // REST operations are passthrough and normally bypass this script entirely.
                return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            }

            return await HandleInvokeMcpAsync(correlationId, start).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new Dictionary<string, string>
            {
                ["Connector"] = ConnectorName,
                ["CorrelationId"] = correlationId,
                ["Operation"] = this.Context.OperationId ?? string.Empty,
                ["ErrorMessage"] = ex.Message,
                ["ErrorType"] = ex.GetType().Name,
                ["StackTrace"] = ex.StackTrace ?? string.Empty,
                ["DurationMs"] = (DateTime.UtcNow - start).TotalMilliseconds.ToString("F0")
            }).ConfigureAwait(false);

            return CreateJsonRpcErrorResponse(null, -32603, "Internal error", ex.Message);
        }
    }

    private async Task<HttpResponseMessage> HandleInvokeMcpAsync(string correlationId, DateTime start)
    {
        var requestBody = this.Context.Request.Content == null
            ? string.Empty
            : await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject requestJson = null;
        if (!string.IsNullOrWhiteSpace(requestBody))
        {
            try
            {
                requestJson = JObject.Parse(requestBody);
            }
            catch (JsonReaderException ex)
            {
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", ex.Message);
            }
        }

        var mcpMethod = requestJson?["method"]?.ToString();
        var requestId = requestJson?["id"];
        var toolName = requestJson?["params"]?["name"]?.ToString();

        var forward = new HttpRequestMessage(HttpMethod.Post, this.Context.Request.RequestUri)
        {
            Content = new StringContent(requestBody ?? string.Empty, Encoding.UTF8, "application/json")
        };

        CopyRequestHeaders(this.Context.Request, forward);

        // The MRC MCP Server returns 406 unless the caller accepts both JSON and SSE.
        forward.Headers.Accept.Clear();
        forward.Headers.Accept.ParseAdd("application/json");
        forward.Headers.Accept.ParseAdd("text/event-stream");

        var response = await this.Context.SendAsync(forward, this.CancellationToken).ConfigureAwait(false);

        var responseBody = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var contentType = response.Content?.Headers?.ContentType?.MediaType ?? string.Empty;
        if (contentType.IndexOf("event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            responseBody = ExtractJsonFromEventStream(responseBody);
        }

        await LogToAppInsights("McpRequestCompleted", new Dictionary<string, string>
        {
            ["Connector"] = ConnectorName,
            ["CorrelationId"] = correlationId,
            ["Method"] = mcpMethod ?? string.Empty,
            ["Tool"] = toolName ?? string.Empty,
            ["StatusCode"] = ((int)response.StatusCode).ToString(),
            ["DurationMs"] = (DateTime.UtcNow - start).TotalMilliseconds.ToString("F0")
        }).ConfigureAwait(false);

        // Notifications are answered upstream with 202 and no body, but Copilot Studio
        // requires a valid JSON-RPC envelope for every request.
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            if (response.IsSuccessStatusCode)
            {
                return CreateJsonRpcSuccessResponse(requestId, new JObject());
            }

            return CreateJsonRpcErrorResponse(
                requestId,
                -32603,
                "Upstream request failed",
                "The Microsoft Release Communications MCP Server returned " + (int)response.StatusCode + " with no content.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return CreateJsonRpcErrorResponse(
                requestId,
                -32603,
                "Upstream request failed",
                Truncate(responseBody, 2000));
        }

        if (string.Equals(mcpMethod, "tools/list", StringComparison.OrdinalIgnoreCase))
        {
            responseBody = NormalizeToolsList(responseBody);
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Collapses a Streamable HTTP (SSE) payload into the single JSON-RPC message it carries.
    /// </summary>
    private static string ExtractJsonFromEventStream(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var current = new StringBuilder();
        string lastPayload = null;
        string bestPayload = null;

        Action flush = () =>
        {
            if (current.Length == 0)
                return;

            var payload = current.ToString();
            current.Clear();

            JObject parsed;
            if (!TryParseJson(payload, out parsed))
                return;

            lastPayload = payload;
            if (parsed["result"] != null || parsed["error"] != null)
                bestPayload = payload;
        };

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                flush();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line.Substring(5).TrimStart(' ');
            if (current.Length > 0)
                current.Append('\n');

            current.Append(data);
        }

        flush();

        return bestPayload ?? lastPayload ?? string.Empty;
    }

    /// <summary>
    /// Rewrites upstream tool schemas into the JSON Schema subset Power Platform accepts.
    /// </summary>
    private static string NormalizeToolsList(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        try
        {
            var responseJson = JObject.Parse(content);
            var tools = responseJson["result"]?["tools"] as JArray;
            if (tools == null)
                return content;

            foreach (var tool in tools.OfType<JObject>())
            {
                // "execution" is a vendor extension that is not part of the MCP tool contract.
                tool.Remove("execution");

                var inputSchema = tool["inputSchema"] as JObject;
                if (inputSchema == null)
                {
                    tool["inputSchema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject()
                    };
                    continue;
                }

                FixSchema(inputSchema);

                if (inputSchema["type"] == null)
                    inputSchema["type"] = "object";

                if (inputSchema["properties"] == null)
                    inputSchema["properties"] = new JObject();
            }

            return responseJson.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch
        {
            return content;
        }
    }

    private static void FixSchema(JObject schema)
    {
        if (schema == null)
            return;

        NormalizeTypeUnion(schema);
        RemoveNullDefault(schema);
        ConvertConstraintToBoolean(schema, "exclusiveMinimum");
        ConvertConstraintToBoolean(schema, "exclusiveMaximum");

        var props = schema["properties"] as JObject;
        if (props != null)
        {
            foreach (var prop in props.Properties())
            {
                var child = prop.Value as JObject;
                if (child != null)
                    FixSchema(child);
            }
        }

        var items = schema["items"] as JObject;
        if (items != null)
            FixSchema(items);

        var additional = schema["additionalProperties"] as JObject;
        if (additional != null)
            FixSchema(additional);

        FixComposite(schema, "oneOf");
        FixComposite(schema, "anyOf");
        FixComposite(schema, "allOf");
    }

    private static void FixComposite(JObject schema, string key)
    {
        var arr = schema[key] as JArray;
        if (arr == null)
            return;

        foreach (var item in arr.OfType<JObject>())
            FixSchema(item);
    }

    /// <summary>
    /// Converts nullable union types such as ["integer","null"] into a single scalar type.
    /// </summary>
    private static void NormalizeTypeUnion(JObject schema)
    {
        var typeArray = schema["type"] as JArray;
        if (typeArray == null)
            return;

        var concrete = typeArray
            .Where(t => t.Type == JTokenType.String)
            .Select(t => t.Value<string>())
            .FirstOrDefault(t => !string.Equals(t, "null", StringComparison.OrdinalIgnoreCase));

        schema["type"] = concrete ?? "string";
    }

    private static void RemoveNullDefault(JObject schema)
    {
        var value = schema["default"];
        if (value != null && value.Type == JTokenType.Null)
            schema.Remove("default");
    }

    private static void ConvertConstraintToBoolean(JObject schema, string key)
    {
        var value = schema[key];
        if (value == null || value.Type == JTokenType.Boolean)
            return;

        if (value.Type == JTokenType.Integer)
        {
            schema[key] = value.Value<int>() != 0;
            return;
        }

        if (value.Type == JTokenType.Float)
        {
            schema[key] = Math.Abs(value.Value<double>()) > 0;
            return;
        }

        if (value.Type == JTokenType.String)
        {
            var s = value.Value<string>();
            bool parsed;
            if (!bool.TryParse(s, out parsed))
                parsed = !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "0", StringComparison.OrdinalIgnoreCase);

            schema[key] = parsed;
        }
    }

    private static void CopyRequestHeaders(HttpRequestMessage source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;

            // The MRC MCP Server is anonymous; never forward caller credentials.
            if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(header.Key, "Accept", StringComparison.OrdinalIgnoreCase))
                continue;

            target.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static bool TryParseJson(string value, out JObject parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            parsed = JObject.Parse(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength) + "...";
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id ?? JValue.CreateNull()
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };

        if (!string.IsNullOrEmpty(data))
            error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id ?? JValue.CreateNull()
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private async Task LogToAppInsights(string eventName, IDictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

        try
        {
            var payload = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(properties ?? new Dictionary<string, string>())
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    payload.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Silent fail for telemetry
        }
    }
}
