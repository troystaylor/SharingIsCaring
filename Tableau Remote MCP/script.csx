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
    private const string TableauRestApiVersion = "3.25";
    private const string AppInsightsConnectionString = "";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var start = DateTime.UtcNow;

        try
        {
            if (!string.Equals(this.Context.OperationId, "InvokeMCP", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"Unsupported operation\"}", Encoding.UTF8, "application/json")
                };
            }

            return await HandleInvokeMcpAsync(correlationId, start).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                Operation = this.Context.OperationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name,
                DurationMs = (DateTime.UtcNow - start).TotalMilliseconds
            }).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    new JObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["error"] = new JObject
                        {
                            ["code"] = -32603,
                            ["message"] = "Internal error",
                            ["data"] = ex.Message
                        },
                        ["id"] = null
                    }.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private async Task<HttpResponseMessage> HandleInvokeMcpAsync(string correlationId, DateTime start)
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var mcpMethod = TryGetMcpMethod(requestBody);

        var forward = new HttpRequestMessage(HttpMethod.Post, this.Context.Request.RequestUri)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };

        CopyRequestHeaders(this.Context.Request, forward);

        var authMode = await ApplyAuthenticationAsync(forward).ConfigureAwait(false);

        var response = await this.Context.SendAsync(forward, this.CancellationToken).ConfigureAwait(false);

        await LogToAppInsights("McpRequestCompleted", new
        {
            CorrelationId = correlationId,
            Method = mcpMethod,
            AuthMode = authMode,
            StatusCode = (int)response.StatusCode,
            DurationMs = (DateTime.UtcNow - start).TotalMilliseconds
        }).ConfigureAwait(false);

        if (!string.Equals(mcpMethod, "tools/list", StringComparison.OrdinalIgnoreCase))
            return response;

        var contentType = response.Content?.Headers?.ContentType?.MediaType;
        if (contentType == null || !contentType.Contains("json"))
            return response;

        var original = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var normalized = NormalizeExclusiveConstraints(original);

        if (string.Equals(original, normalized, StringComparison.Ordinal))
            return response;

        var rewritten = new HttpResponseMessage(response.StatusCode)
        {
            ReasonPhrase = response.ReasonPhrase,
            Content = new StringContent(normalized, Encoding.UTF8, "application/json")
        };

        CopyResponseHeaders(response, rewritten);
        return rewritten;
    }

    private async Task<string> ApplyAuthenticationAsync(HttpRequestMessage forward)
    {
        if (forward.Headers.Authorization != null)
            return "oauth-header";

        var configuredBearer = GetHeader("x-oauth-bearer-token");
        if (!string.IsNullOrWhiteSpace(configuredBearer))
        {
            forward.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuredBearer.Trim());
            return "oauth-configured-token";
        }

        var (patName, patSecret) = DecodeBasicAuth();
        var tableauServerUrl = GetHeader("x-tableau-server-url");
        var siteName = GetHeader("x-tableau-site-name") ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(patName)
            && !string.IsNullOrWhiteSpace(patSecret)
            && !string.IsNullOrWhiteSpace(tableauServerUrl))
        {
            var tableauToken = await SignInWithPatAsync(tableauServerUrl, siteName, patName, patSecret).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(tableauToken))
            {
                RemoveHeader(forward, "X-Tableau-Auth");
                forward.Headers.TryAddWithoutValidation("X-Tableau-Auth", tableauToken);
                return "pat";
            }
        }

        return "none";
    }

    private async Task<string> SignInWithPatAsync(string tableauServerUrl, string siteName, string patName, string patSecret)
    {
        var url = tableauServerUrl.TrimEnd('/') + "/api/" + TableauRestApiVersion + "/auth/signin";

        var payload = new JObject
        {
            ["credentials"] = new JObject
            {
                ["personalAccessTokenName"] = patName,
                ["personalAccessTokenSecret"] = patSecret,
                ["site"] = new JObject
                {
                    ["contentUrl"] = siteName ?? string.Empty
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var obj = JObject.Parse(text);
            return obj["credentials"]?["token"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string TryGetMcpMethod(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        try
        {
            var obj = JObject.Parse(body);
            return obj["method"]?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeExclusiveConstraints(string content)
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
                var inputSchema = tool["inputSchema"] as JObject;
                if (inputSchema != null)
                {
                    FixSchema(inputSchema);
                }
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

            if (header.Key.StartsWith("x-tableau-", StringComparison.OrdinalIgnoreCase))
                continue;

            target.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpResponseMessage target)
    {
        foreach (var header in source.Headers)
        {
            target.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static void RemoveHeader(HttpRequestMessage request, string name)
    {
        if (request.Headers.Contains(name))
            request.Headers.Remove(name);
    }

    private string GetHeader(string name)
    {
        IEnumerable<string> values;
        if (this.Context.Request.Headers.TryGetValues(name, out values))
            return values.FirstOrDefault();

        return null;
    }

    private (string username, string password) DecodeBasicAuth()
    {
        var auth = this.Context.Request.Headers.Authorization?.ToString();
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Substring(6)));
            var parts = raw.Split(new[] { ':' }, 2);
            return (parts[0], parts.Length > 1 ? parts[1] : null);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task LogToAppInsights(string eventName, object properties)
    {
        if (string.IsNullOrWhiteSpace(AppInsightsConnectionString))
            return;

        try
        {
            var ikey = ExtractConnectionStringPart(AppInsightsConnectionString, "InstrumentationKey");
            var endpoint = ExtractConnectionStringPart(AppInsightsConnectionString, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrWhiteSpace(ikey))
                return;

            var payload = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["iKey"] = ikey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(properties ?? new { })
                    }
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/v2/track")
            {
                Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        var parts = connectionString.Split(';');
        foreach (var p in parts)
        {
            var kv = p.Split(new[] { '=' }, 2);
            if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
                return kv[1];
        }

        return null;
    }
}
