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
    // ─── Configuration ────────────────────────────────────────────────────────
    private const string BACKEND_HOST = "your-aca-app.azurecontainerapps.io";

    // App Insights (optional)
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;

        switch (operationId)
        {
            case "InvokeMCP":
                return await ForwardToBackend("/mcp");
            case "SearchImages":
                return await ForwardToBackend("/api/search");
            case "SearchByImage":
                return await ForwardToBackend("/api/search-by-image");
            case "UploadImage":
                return await ForwardToBackend("/api/upload");
            case "GetImageDetails":
                return await ForwardToBackend(null); // Path already in URL
            case "GetImageUrl":
                return await ForwardToBackend(null); // Path already in URL
            default:
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent($"Unknown operation: {operationId}")
                };
        }
    }

    private async Task<HttpResponseMessage> ForwardToBackend(string path)
    {
        var request = this.Context.Request;

        // Rewrite host to backend
        var uriBuilder = new UriBuilder(request.RequestUri)
        {
            Host = BACKEND_HOST,
            Port = 443,
            Scheme = "https"
        };

        if (path != null)
        {
            uriBuilder.Path = path;
        }

        request.RequestUri = uriBuilder.Uri;

        // Forward the API key as-is (backend validates X-API-Key header)
        try
        {
            var response = await this.Context.SendAsync(request, this.CancellationToken);
            await LogToAppInsights("RequestForwarded", new Dictionary<string, string>
            {
                { "operation", this.Context.OperationId },
                { "status", response.StatusCode.ToString() }
            });
            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestFailed", new Dictionary<string, string>
            {
                { "operation", this.Context.OperationId },
                { "error", ex.Message }
            });
            throw;
        }
    }

    private async Task LogToAppInsights(string eventName, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

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

            var json = JsonConvert.SerializeObject(telemetryData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var logRequest = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = content
            };

            await this.Context.SendAsync(logRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
