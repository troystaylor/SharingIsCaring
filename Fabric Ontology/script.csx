using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private const string ConnectorName = "Fabric Ontology";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = ResolveOperationId(this.Context.OperationId);

        try
        {
            switch (operationId)
            {
                case "CreateOntology":
                case "UpdateOntologyDefinition":
                    return await this.HandleDefinitionWriteAsync(operationId).ConfigureAwait(false);

                case "GetOntologyDefinition":
                case "GetOperationResult":
                    return await this.HandleDefinitionReadAsync(operationId).ConfigureAwait(false);

                default:
                    return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await this.LogExceptionToAppInsightsAsync(operationId, ex).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Some regions return Context.OperationId base64 encoded. Decode when the value round
    /// trips cleanly, otherwise treat it as already plain text.
    /// </summary>
    private static string ResolveOperationId(string operationId)
    {
        if (string.IsNullOrEmpty(operationId))
        {
            return string.Empty;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(operationId));

            if (!string.IsNullOrEmpty(decoded)
                && Convert.ToBase64String(Encoding.UTF8.GetBytes(decoded)) == operationId)
            {
                return decoded;
            }
        }
        catch (FormatException)
        {
            // Not base64, so the raw value is already the operation ID.
        }
        catch (ArgumentException)
        {
            // Decoded bytes are not valid UTF-8, so the raw value is the operation ID.
        }

        return operationId;
    }

    /// <summary>
    /// Encodes any readable payloadText on definition parts into base64 payload before
    /// forwarding the request, so callers never have to base64 encode by hand.
    /// </summary>
    private async Task<HttpResponseMessage> HandleDefinitionWriteAsync(string operationId)
    {
        if (this.Context.Request.Content != null)
        {
            var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

            var body = TryParseObject(raw);

            if (body != null)
            {
                EncodeDefinitionParts(body["definition"] as JObject);
                this.Context.Request.Content = ToJsonContent(body);
            }
        }

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return await this.NormalizeResponseAsync(operationId, response).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleDefinitionReadAsync(string operationId)
    {
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return await this.NormalizeResponseAsync(operationId, response).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a bare 202 into a usable body carrying the operation ID, and decodes any
    /// base64 definition part payloads into a readable payloadText sibling.
    /// </summary>
    private async Task<HttpResponseMessage> NormalizeResponseAsync(string operationId, HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            var accepted = new JObject
            {
                ["status"] = "Accepted",
                ["operationId"] = GetHeaderValue(response, "x-ms-operation-id"),
                ["location"] = response.Headers.Location != null
                    ? response.Headers.Location.ToString()
                    : GetHeaderValue(response, "Location")
            };

            var retryAfter = GetHeaderValue(response, "Retry-After");
            int retrySeconds;
            if (int.TryParse(retryAfter, out retrySeconds))
            {
                accepted["retryAfter"] = retrySeconds;
            }

            response.Content = ToJsonContent(accepted);
            await this.LogEventToAppInsightsAsync(operationId, "Accepted").ConfigureAwait(false);
            return response;
        }

        if (!response.IsSuccessStatusCode || response.Content == null)
        {
            await this.LogEventToAppInsightsAsync(operationId, ((int)response.StatusCode).ToString()).ConfigureAwait(false);
            return response;
        }

        var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = TryParseObject(raw);

        if (body != null)
        {
            DecodeDefinitionParts(body["definition"] as JObject);
            response.Content = ToJsonContent(body);
        }

        await this.LogEventToAppInsightsAsync(operationId, "Succeeded").ConfigureAwait(false);
        return response;
    }

    private static void EncodeDefinitionParts(JObject definition)
    {
        var parts = definition != null ? definition["parts"] as JArray : null;
        if (parts == null)
        {
            return;
        }

        foreach (var part in parts.OfType<JObject>())
        {
            var payloadText = (string)part["payloadText"];
            part.Remove("payloadText");

            if (string.IsNullOrEmpty(payloadText))
            {
                continue;
            }

            part["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadText));
            part["payloadType"] = "InlineBase64";
        }
    }

    private static void DecodeDefinitionParts(JObject definition)
    {
        var parts = definition != null ? definition["parts"] as JArray : null;
        if (parts == null)
        {
            return;
        }

        foreach (var part in parts.OfType<JObject>())
        {
            var payload = (string)part["payload"];
            if (string.IsNullOrEmpty(payload))
            {
                continue;
            }

            try
            {
                part["payloadText"] = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            }
            catch (FormatException)
            {
                // Payload is not base64. Leave the part untouched rather than failing the call.
            }
        }
    }

    private static StringContent ToJsonContent(JObject body)
    {
        return CreateJsonContent(body.ToString(Newtonsoft.Json.Formatting.None));
    }

    /// <summary>
    /// Parses a JSON object, returning null for empty payloads or content that is not a
    /// JSON object, so non-JSON responses pass through untouched.
    /// </summary>
    private static JObject TryParseObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JObject.Parse(raw);
        }
        catch (JsonReaderException)
        {
            return null;
        }
    }

    private static string GetHeaderValue(HttpResponseMessage response, string name)
    {
        IEnumerable<string> values;

        if (response.Headers.TryGetValues(name, out values))
        {
            return values.FirstOrDefault();
        }

        if (response.Content != null && response.Content.Headers.TryGetValues(name, out values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }

    private Task LogEventToAppInsightsAsync(string operationId, string outcome)
    {
        return this.LogToAppInsightsAsync("FabricOntologyOperation", new Dictionary<string, string>
        {
            { "connector", ConnectorName },
            { "operationId", operationId },
            { "outcome", outcome }
        });
    }

    private Task LogExceptionToAppInsightsAsync(string operationId, Exception ex)
    {
        return this.LogToAppInsightsAsync("FabricOntologyException", new Dictionary<string, string>
        {
            { "connector", ConnectorName },
            { "operationId", operationId },
            { "exceptionType", ex.GetType().FullName },
            { "message", ex.Message },
            { "stackTrace", ex.StackTrace ?? string.Empty }
        });
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
