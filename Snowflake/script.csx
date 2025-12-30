using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Net;
using System.Text;

public class Script : ScriptBase
{
    // Application Insights connection string - update this value to change the logging target
    private const string APP_INSIGHTS_CONNECTION_STRING = "[YOUR_STRING]";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var _logger = this.Context.Logger;
        var request = this.Context.Request;
        var requestBody = await request.Content.ReadAsStringAsync();
        var startTime = DateTime.UtcNow;
        
        // Use hardcoded Application Insights connection string
        var appInsightsConnectionString = APP_INSIGHTS_CONNECTION_STRING;
        var loggingEnabled = !string.IsNullOrEmpty(appInsightsConnectionString);

        if (loggingEnabled)
        {
            _logger.LogInformation($"OriginalRequest - Body: {requestBody}, UserAgent: {request.Headers.UserAgent?.ToString()}");
            await LogToAppInsights(appInsightsConnectionString, "OriginalRequest", new
            {
                RequestBody = requestBody,
                UserAgent = request.Headers.UserAgent?.ToString(),
                ContentType = request.Content?.Headers?.ContentType?.ToString(),
                RequestUri = request.RequestUri?.ToString()
            });
        }

        // Parse the incoming request
        JObject requestObject = null;
        var parseError = false;
        try
        {
            var parsed = JsonConvert.DeserializeObject(requestBody);
            
            // If it's an array, take the first element
            if (parsed is JArray array && array.Count > 0)
            {
                requestObject = array[0] as JObject;
            }
            else if (parsed is JObject obj)
            {
                requestObject = obj;
            }
        }
        catch (Exception ex)
        {
            parseError = true;
            requestObject = new JObject();
            
            if (loggingEnabled)
            {
                _logger.LogInformation($"ParseError - Error: {ex.Message}, OriginalBody: {requestBody}");
                await LogToAppInsights(appInsightsConnectionString, "ParseError", new
                {
                    Error = ex.Message,
                    OriginalBody = requestBody
                });
            }
        }

        // Ensure requestObject is not null
        if (requestObject == null)
        {
            requestObject = new JObject();
        }

        // Track transformations applied
        var transformations = new List<string>();

        // Normalize JSON-RPC id to an integer
        var idToken = requestObject["id"];
        var originalIdType = idToken?.Type.ToString() ?? "null";
        
        if (idToken == null)
        {
            requestObject["id"] = 1;
            transformations.Add("id:null->1");
        }
        else
        {
            if (idToken.Type == JTokenType.Integer)
            {
                // already integer, do nothing
            }
            else if (idToken.Type == JTokenType.Float)
            {
                requestObject["id"] = (int)idToken.Value<double>();
                transformations.Add($"id:float->int");
            }
            else if (idToken.Type == JTokenType.String)
            {
                if (int.TryParse(idToken.ToString(), out var parsed))
                {
                    requestObject["id"] = parsed;
                    transformations.Add($"id:string->int");
                }
                else
                {
                    requestObject["id"] = 1;
                    transformations.Add($"id:invalid_string->1");
                }
            }
            else if (idToken.Type == JTokenType.Boolean)
            {
                requestObject["id"] = idToken.Value<bool>() ? 1 : 0;
                transformations.Add($"id:bool->int");
            }
            else
            {
                requestObject["id"] = 1;
                transformations.Add($"id:{idToken.Type}->1");
            }
        }
        
        // If method is missing, default to tools/list
        var originalMethod = requestObject["method"]?.ToString();
        if (requestObject["method"] == null || string.IsNullOrEmpty(originalMethod))
        {
            requestObject["method"] = "tools/list";
            transformations.Add("method:null->tools/list");
        }

        // Handle notifications/initialized - return acknowledgment without forwarding to Snowflake
        if (requestObject["method"]?.ToString() == "notifications/initialized")
        {
            if (loggingEnabled)
            {
                _logger.LogInformation($"NotificationHandled - Method: notifications/initialized, Action: Return acknowledgment");
                await LogToAppInsights(appInsightsConnectionString, "NotificationHandled", new
                {
                    Method = "notifications/initialized",
                    Action = "Return acknowledgment"
                });
            }
            
            // Return a minimal acknowledgment (not a tools/list response)
            var ackResponse = new JObject
            {
                { "jsonrpc", "2.0" },
                { "method", "notifications/initialized" }
            };
            
            var ackContent = JsonConvert.SerializeObject(ackResponse);
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ackContent, Encoding.UTF8, "application/json")
            };
            
            if (loggingEnabled)
            {
                await LogToAppInsights(appInsightsConnectionString, "NotificationAcknowledged", new
                {
                    Method = "notifications/initialized",
                    Response = ackContent
                });
            }
            
            return httpResponse;
        }

        // Ensure params object exists (preserve existing params if present)
        JObject paramsObj = requestObject["params"] as JObject ?? new JObject();

        // For initialize method, inject or update protocolVersion to 2025-06-18
        if (requestObject["method"]?.ToString() == "initialize")
        {
            var currentProtocolVersion = paramsObj["protocolVersion"]?.ToString();
            if (currentProtocolVersion == null)
            {
                paramsObj["protocolVersion"] = "2025-06-18";
                transformations.Add("params.protocolVersion:null->2025-06-18");
            }
            else if (currentProtocolVersion != "2025-06-18")
            {
                paramsObj["protocolVersion"] = "2025-06-18";
                transformations.Add($"params.protocolVersion:{currentProtocolVersion}->2025-06-18");
            }
        }

        // Update params in request
        requestObject["params"] = paramsObj;

        // Serialize the complete JSON-RPC request
        var modifiedBody = JsonConvert.SerializeObject(requestObject);

        if (loggingEnabled)
        {
            _logger.LogInformation($"TransformedRequest - OriginalMethod: {originalMethod}, FinalMethod: {requestObject["method"]?.ToString()}, OriginalIdType: {originalIdType}, FinalId: {requestObject["id"]}, Transformations: {string.Join(",", transformations)}, ParseError: {parseError}, ModifiedBody: {modifiedBody}");
            await LogToAppInsights(appInsightsConnectionString, "TransformedRequest", new
            {
                OriginalMethod = originalMethod,
                FinalMethod = requestObject["method"]?.ToString(),
                OriginalIdType = originalIdType,
                FinalId = requestObject["id"],
                Transformations = transformations,
                ParseError = parseError,
                ModifiedBody = modifiedBody
            });
        }

        // Create new request with the constructed body
        var modifiedRequest = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = new StringContent(modifiedBody, Encoding.UTF8, "application/json")
        };

        // Copy headers from original request
        foreach (var header in request.Headers)
        {
            modifiedRequest.Headers.Add(header.Key, header.Value);
        }

        // Add User-Agent if missing (Copilot Studio doesn't send one)
        if (!modifiedRequest.Headers.Contains("User-Agent") || 
            (modifiedRequest.Headers.UserAgent != null && modifiedRequest.Headers.UserAgent.Count == 0))
        {
            modifiedRequest.Headers.Add("User-Agent", "Snowflake-MCP-Connector/1.0");
        }

        // Send the modified request
        var response = await this.Context.SendAsync(modifiedRequest, this.CancellationToken).ConfigureAwait(false);
        var latencyMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // If response body is the malformed array [{"jsonrpc":"2.0"}], blank it out to avoid confusing upstream
        var wasMalformed = false;
        if (response?.Content != null)
        {
            var respBody = (await response.Content.ReadAsStringAsync().ConfigureAwait(false))?.Trim();
            
            if (respBody == "[{\"jsonrpc\":\"2.0\"}]")
            {
                wasMalformed = true;
                response.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            }

            if (loggingEnabled)
            {
                _logger.LogInformation($"SnowflakeResponse - StatusCode: {(int)response.StatusCode}, ResponseBody: {(wasMalformed ? "[MALFORMED - BLANKED]" : respBody?.Substring(0, Math.Min(1000, respBody?.Length ?? 0)))}, WasMalformed: {wasMalformed}, LatencyMs: {latencyMs}, Method: {requestObject["method"]?.ToString()}");
                await LogToAppInsights(appInsightsConnectionString, "SnowflakeResponse", new
                {
                    StatusCode = (int)response.StatusCode,
                    ResponseBody = wasMalformed ? "[MALFORMED - BLANKED]" : respBody?.Substring(0, Math.Min(1000, respBody?.Length ?? 0)),
                    WasMalformed = wasMalformed,
                    LatencyMs = latencyMs,
                    Method = requestObject["method"]?.ToString()
                });
            }
        }

        return response;
    }

    private async Task LogToAppInsights(string connectionString, string eventName, object properties)
    {
        try
        {
            // Parse connection string to get Instrumentation Key
            var instrumentationKey = ExtractInstrumentationKey(connectionString);
            if (string.IsNullOrEmpty(instrumentationKey))
            {
                return;
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = properties
                    }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri("https://dc.services.visualstudio.com/v2/track");
            
            HttpRequestMessage telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            
            await this.Context.SendAsync(telemetryRequest, this.CancellationToken);
        }
        catch
        {
            // Suppress logging errors to avoid breaking connector
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        try
        {
            // Connection string format: InstrumentationKey=xxxxx;IngestionEndpoint=https://...
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("InstrumentationKey=".Length);
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}

