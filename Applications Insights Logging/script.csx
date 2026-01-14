using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// Template script for Power Platform custom connectors with Application Insights telemetry
/// This demonstrates:
/// 1. Application Insights integration for telemetry
/// 2. this.Context.Logger usage for basic logging
/// 3. Error handling with logging
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // CONFIGURATION - Update this value to enable Application Insights
    // ========================================
    /// <summary>
    /// Application Insights connection string
    /// Format: InstrumentationKey=YOUR-KEY-HERE;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;LiveEndpoint=https://REGION.livediagnostics.monitor.azure.com/
    /// Get from: Azure Portal → Application Insights resource → Overview → Connection String
    /// Leave empty to disable telemetry
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Generate correlation ID for tracking requests across logs
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        
        // Basic logging using this.Context.Logger (always available)
        this.Context.Logger.LogInformation($"Request received. CorrelationId: {correlationId}");
        
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var requestPath = this.Context.Request.RequestUri.AbsolutePath;
            
            // Enhanced telemetry logging to Application Insights (optional)
            await LogToAppInsights("RequestReceived", new { 
                CorrelationId = correlationId,
                Path = requestPath,
                Method = this.Context.Request.Method.Method,
                UserAgent = this.Context.Request.Headers.UserAgent?.ToString(),
                BodyPreview = body?.Substring(0, Math.Min(500, body?.Length ?? 0))
            });
            
            if (string.IsNullOrWhiteSpace(body))
            {
                this.Context.Logger.LogWarning("Empty request body received");
                return CreateErrorResponse("Request body is required", 400);
            }

            var payload = JObject.Parse(body);
            
            // Example: Process your custom operation
            var operationName = payload["operation"]?.ToString() ?? "unknown";
            this.Context.Logger.LogInformation($"Processing operation: {operationName}");
            
            await LogToAppInsights("OperationProcessed", new { 
                CorrelationId = correlationId,
                Operation = operationName,
                HasPayload = payload.Count > 0
            });
            
            // TODO: Add your custom logic here
            var result = new JObject
            {
                ["success"] = true,
                ["correlationId"] = correlationId,
                ["message"] = $"Operation '{operationName}' processed successfully"
            };
            
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(result.ToString(), Encoding.UTF8, "application/json")
            };
        }
        catch (Exception ex)
        {
            // Log errors to both Context.Logger and Application Insights
            var errorMessage = $"Unexpected error: {ex.Message}";
            this.Context.Logger.LogError($"CorrelationId: {correlationId}, Error: {errorMessage}, StackTrace: {ex.StackTrace}");
            
            await LogToAppInsights("RequestError", new { 
                CorrelationId = correlationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name,
                StackTrace = ex.StackTrace?.Substring(0, Math.Min(1000, ex.StackTrace?.Length ?? 0))
            });
            
            return CreateErrorResponse(errorMessage, 500);
        }
        finally
        {
            // Log request completion with duration
            var duration = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"Request completed. CorrelationId: {correlationId}, Duration: {duration.TotalMilliseconds}ms");
            
            await LogToAppInsights("RequestCompleted", new { 
                CorrelationId = correlationId,
                DurationMs = duration.TotalMilliseconds
            });
        }
    }

    // ========================================
    // APPLICATION INSIGHTS TELEMETRY
    // ========================================
    
    /// <summary>
    /// Send custom event to Application Insights
    /// This provides rich telemetry beyond basic this.Context.Logger
    /// </summary>
    /// <param name="eventName">Name of the event (e.g., "ToolExecuted", "APICallSuccess")</param>
    /// <param name="properties">Anonymous object with properties to log</param>
    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            // Extract connection string components
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);
            
            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
            {
                // Telemetry disabled - connection string not configured
                return;
            }

            // Convert properties object to dictionary of strings
            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = Newtonsoft.Json.Linq.JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            // Build Application Insights telemetry payload
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
                        properties = propsDict
                    }
                }
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            // Send telemetry asynchronously (fire and forget)
            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Suppress telemetry errors - don't fail the main request
            // Optionally log to Context.Logger for debugging
            this.Context.Logger.LogWarning($"Telemetry error: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract Instrumentation Key from Application Insights connection string
    /// </summary>
    private string ExtractInstrumentationKey(string connectionString)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionString)) return null;
            
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

    /// <summary>
    /// Extract Ingestion Endpoint from Application Insights connection string
    /// </summary>
    private string ExtractIngestionEndpoint(string connectionString)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionString)) 
                return "https://dc.services.visualstudio.com/";
            
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("IngestionEndpoint=".Length);
                }
            }
            return "https://dc.services.visualstudio.com/";
        }
        catch
        {
            return "https://dc.services.visualstudio.com/";
        }
    }

    // ========================================
    // HELPER METHODS
    // ========================================
    
    private HttpResponseMessage CreateErrorResponse(string message, int statusCode)
    {
        var error = new JObject
        {
            ["error"] = new JObject
            {
                ["message"] = message,
                ["statusCode"] = statusCode
            }
        };
        return new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(error.ToString(), Encoding.UTF8, "application/json")
        };
    }
}
