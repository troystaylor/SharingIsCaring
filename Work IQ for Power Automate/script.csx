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
    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private const string ConnectorName = "Work IQ for Power Automate";
    private const string McpEndpoint = "https://workiq.svc.cloud.microsoft/mcp";
    private const string A2AEndpoint = "https://workiq.svc.cloud.microsoft/a2a";

    /// <summary>
    /// Maps each scripted operation onto the Work IQ MCP tool that backs it.
    /// </summary>
    private static readonly Dictionary<string, string> ToolMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AskAgent"] = "ask",
            ["ListAgents"] = "list_agents",
            ["SearchPaths"] = "search_paths",
            ["GetSchema"] = "get_schema",
            ["FetchEntities"] = "fetch",
            ["CreateEntity"] = "create_entity",
            ["UpdateEntity"] = "update_entity",
            ["DeleteEntity"] = "delete_entity",
            ["DoAction"] = "do_action",
            ["CallFunction"] = "call_function"
        };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId ?? string.Empty;
        var start = DateTime.UtcNow;

        try
        {
            if (string.Equals(operationId, "AskWorkIQ", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleAskWorkIQAsync(start).ConfigureAwait(false);
            }

            string toolName;
            if (ToolMap.TryGetValue(operationId, out toolName))
            {
                return await HandleToolAsync(operationId, toolName, start).ConfigureAwait(false);
            }

            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("RequestError", new Dictionary<string, string>
            {
                ["Connector"] = ConnectorName,
                ["Operation"] = operationId,
                ["ErrorMessage"] = ex.Message,
                ["ErrorType"] = ex.GetType().Name,
                ["StackTrace"] = ex.StackTrace ?? string.Empty,
                ["DurationMs"] = (DateTime.UtcNow - start).TotalMilliseconds.ToString("F0")
            }).ConfigureAwait(false);

            return CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Turns a plain question into an A2A message/send envelope and flattens the task it returns.
    /// </summary>
    private async Task<HttpResponseMessage> HandleAskWorkIQAsync(DateTime start)
    {
        JObject input = null;
        if (this.Context.Request.Content != null)
        {
            var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    input = JObject.Parse(raw);
                }
                catch (JsonReaderException ex)
                {
                    return CreateErrorResponse(HttpStatusCode.BadRequest, "Request body is not valid JSON.", ex.Message);
                }
            }
        }

        var question = input?["question"]?.ToString();
        if (string.IsNullOrWhiteSpace(question))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "Question is required.");
        }

        // Work IQ runs A2A 0.3.0: kind discriminators are mandatory and roles are lowercase.
        var message = new JObject
        {
            ["kind"] = "message",
            ["role"] = "user",
            ["messageId"] = Guid.NewGuid().ToString(),
            ["parts"] = new JArray
            {
                new JObject
                {
                    ["kind"] = "text",
                    ["text"] = question
                }
            }
        };

        var contextId = input?["contextId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(contextId))
        {
            message["contextId"] = contextId;
        }

        var envelope = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = "message/send",
            ["params"] = new JObject { ["message"] = message }
        };

        var payload = await SendUpstreamAsync(A2AEndpoint, envelope).ConfigureAwait(false);
        if (payload.Item1 != null)
        {
            return payload.Item1;
        }

        var body = payload.Item2;

        var rpcError = body["error"];
        if (rpcError != null)
        {
            return CreateErrorResponse(
                HttpStatusCode.BadRequest,
                rpcError["message"]?.ToString() ?? "Work IQ returned an error.",
                rpcError["data"]?.ToString());
        }

        var result = body["result"] as JObject;
        var answer = new StringBuilder();

        var artifacts = result?["artifacts"] as JArray;
        if (artifacts != null)
        {
            foreach (var artifact in artifacts.OfType<JObject>())
            {
                var parts = artifact["parts"] as JArray;
                if (parts == null)
                    continue;

                foreach (var part in parts.OfType<JObject>())
                {
                    // Artifacts also carry "data" parts holding display metadata; only text is the answer.
                    if (!string.Equals(part["kind"]?.ToString(), "text", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var text = part["text"]?.ToString();
                    if (string.IsNullOrEmpty(text))
                        continue;

                    if (answer.Length > 0)
                        answer.Append('\n');

                    answer.Append(text);
                }
            }
        }

        var shaped = new JObject
        {
            ["answer"] = answer.ToString(),
            ["contextId"] = result?["contextId"]?.ToString() ?? string.Empty,
            ["taskId"] = result?["id"]?.ToString() ?? string.Empty,
            ["state"] = result?["status"]?["state"]?.ToString() ?? string.Empty
        };

        await LogToAppInsightsAsync("AskCompleted", new Dictionary<string, string>
        {
            ["Connector"] = ConnectorName,
            ["Operation"] = "AskWorkIQ",
            ["State"] = shaped["state"].ToString(),
            ["DurationMs"] = (DateTime.UtcNow - start).TotalMilliseconds.ToString("F0")
        }).ConfigureAwait(false);

        return CreateJsonResponse(HttpStatusCode.OK, shaped);
    }

    /// <summary>
    /// Wraps a typed operation as an MCP tools/call and flattens the content it returns.
    /// </summary>
    private async Task<HttpResponseMessage> HandleToolAsync(string operationId, string toolName, DateTime start)
    {
        JObject input = null;
        if (this.Context.Request.Content != null)
        {
            var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    input = JObject.Parse(raw);
                }
                catch (JsonReaderException ex)
                {
                    return CreateErrorResponse(HttpStatusCode.BadRequest, "Request body is not valid JSON.", ex.Message);
                }
            }
        }

        // Every declared property maps straight onto a tool argument of the same name,
        // so empty values are dropped rather than sent as nulls the tool would reject.
        var arguments = new JObject();
        if (input != null)
        {
            foreach (var property in input.Properties())
            {
                var value = property.Value;

                if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
                    continue;

                if (value.Type == JTokenType.String && string.IsNullOrWhiteSpace(value.ToString()))
                    continue;

                if (value.Type == JTokenType.Array && !value.HasValues)
                    continue;

                if (value.Type == JTokenType.Object && !value.HasValues)
                    continue;

                arguments[property.Name] = value;
            }
        }

        var envelope = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = "tools/call",
            ["params"] = new JObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments
            }
        };

        var payload = await SendUpstreamAsync(McpEndpoint, envelope).ConfigureAwait(false);
        if (payload.Item1 != null)
        {
            return payload.Item1;
        }

        var body = payload.Item2;

        var rpcError = body["error"];
        if (rpcError != null)
        {
            return CreateErrorResponse(
                HttpStatusCode.BadRequest,
                rpcError["message"]?.ToString() ?? "Work IQ returned an error.",
                rpcError["data"]?.ToString());
        }

        var result = body["result"] as JObject;
        var text = new StringBuilder();

        var content = result?["content"] as JArray;
        if (content != null)
        {
            foreach (var item in content.OfType<JObject>())
            {
                var itemText = item["text"]?.ToString();
                if (string.IsNullOrEmpty(itemText))
                    continue;

                if (text.Length > 0)
                    text.Append('\n');

                text.Append(itemText);
            }
        }

        var flattened = text.ToString();
        var isError = result?["isError"]?.Type == JTokenType.Boolean && result["isError"].Value<bool>();

        // Work IQ returns some tools as content text and others as structuredContent
        // with an empty content array, so both shapes have to be unwrapped.
        var toolPayload = result?["structuredContent"];
        if (toolPayload == null || toolPayload.Type == JTokenType.Null)
        {
            toolPayload = ParseJsonToken(flattened);
        }

        await LogToAppInsightsAsync("ToolCompleted", new Dictionary<string, string>
        {
            ["Connector"] = ConnectorName,
            ["Operation"] = operationId,
            ["Tool"] = toolName,
            ["IsError"] = isError.ToString(),
            ["DurationMs"] = (DateTime.UtcNow - start).TotalMilliseconds.ToString("F0")
        }).ConfigureAwait(false);

        // List Copilot agents declares a typed array, so its payload is promoted.
        if (string.Equals(operationId, "ListAgents", StringComparison.OrdinalIgnoreCase))
        {
            var agents = toolPayload as JArray
                ?? (toolPayload as JObject)?["agents"] as JArray
                ?? new JArray();

            return CreateJsonResponse(HttpStatusCode.OK, new JObject { ["agents"] = agents });
        }

        if (string.IsNullOrEmpty(flattened) && toolPayload != null)
        {
            flattened = toolPayload.ToString(Newtonsoft.Json.Formatting.None);
        }

        var shaped = new JObject
        {
            ["isError"] = isError,
            ["text"] = flattened
        };

        if (toolPayload is JObject)
        {
            shaped["data"] = toolPayload;
        }
        else if (toolPayload is JArray)
        {
            // "data" is declared as an object, so an array payload is wrapped to keep the contract stable.
            shaped["data"] = new JObject { ["items"] = toolPayload };
        }

        return CreateJsonResponse(HttpStatusCode.OK, shaped);
    }

    /// <summary>
    /// Posts a JSON-RPC envelope to Work IQ and returns the decoded message, or a failure response.
    /// </summary>
    private async Task<Tuple<HttpResponseMessage, JObject>> SendUpstreamAsync(string endpoint, JObject envelope)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                envelope.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };

        CopyRequestHeaders(this.Context.Request, request);

        // Work IQ answers MCP calls as SSE and rejects callers that do not accept both media types.
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);

        var responseBody = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var contentType = response.Content?.Headers?.ContentType?.MediaType ?? string.Empty;
        if (contentType.IndexOf("event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            responseBody = ExtractJsonFromEventStream(responseBody);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Tuple.Create(
                CreateErrorResponse(
                    response.StatusCode,
                    "Work IQ returned " + (int)response.StatusCode + ".",
                    Truncate(responseBody, 2000)),
                (JObject)null);
        }

        JObject parsed;
        if (!TryParseJson(responseBody, out parsed))
        {
            return Tuple.Create(
                CreateErrorResponse(
                    HttpStatusCode.BadGateway,
                    "Work IQ returned a response that could not be parsed.",
                    Truncate(responseBody, 2000)),
                (JObject)null);
        }

        return Tuple.Create((HttpResponseMessage)null, parsed);
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
    /// Work IQ returns tool payloads as a JSON string inside a text block; this recovers the structure.
    /// </summary>
    private static JToken ParseJsonToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return JToken.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private static void CopyRequestHeaders(HttpRequestMessage source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(header.Key, "Accept", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
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

    private HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, JObject body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string message, string details = null)
    {
        var error = new JObject
        {
            ["message"] = message
        };

        if (!string.IsNullOrEmpty(details))
        {
            error["details"] = details;
        }

        return CreateJsonResponse(statusCode, new JObject { ["error"] = error });
    }

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
            var telemetryData = new
            {
                name = "Microsoft.ApplicationInsights.Event",
                time = DateTime.UtcNow.ToString("O"),
                iKey = APP_INSIGHTS_KEY,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = properties ?? new Dictionary<string, string>()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(telemetryData),
                    Encoding.UTF8,
                    "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
