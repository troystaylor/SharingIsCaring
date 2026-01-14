using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // API endpoints
    private const string POWER_PLATFORM_API = "https://api.powerplatform.com";
    private const string LICENSING_API = "https://licensing.powerplatform.microsoft.com";
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var requestPath = this.Context.Request.RequestUri.AbsolutePath.ToLowerInvariant();
        var operationId = this.Context.OperationId;
        var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var stopwatch = Stopwatch.StartNew();
        
        this.Context.Logger.LogInformation($"[{correlationId}] START {operationId} - Path: {requestPath}");
        
        try
        {
            HttpResponseMessage response;
            
            if (requestPath.EndsWith("/environments"))
            {
                response = await HandleListEnvironmentsAsync(correlationId).ConfigureAwait(false);
            }
            else if (requestPath.EndsWith("/credits"))
            {
                response = await HandleGetCreditsAsync(correlationId).ConfigureAwait(false);
            }
            else if (requestPath.EndsWith("/entitlements"))
            {
                response = await HandleGetEntitlementsAsync(correlationId).ConfigureAwait(false);
            }
            else
            {
                this.Context.Logger.LogWarning($"[{correlationId}] Unknown endpoint requested: {requestPath}");
                response = CreateErrorResponse(HttpStatusCode.NotFound, "Unknown endpoint");
            }
            
            stopwatch.Stop();
            this.Context.Logger.LogInformation($"[{correlationId}] END {operationId} - Status: {(int)response.StatusCode} - Duration: {stopwatch.ElapsedMilliseconds}ms");
            
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            this.Context.Logger.LogError($"[{correlationId}] ERROR {operationId} - {ex.GetType().Name}: {ex.Message} - Duration: {stopwatch.ElapsedMilliseconds}ms");
            return CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
        }
    }
    
    /// <summary>
    /// List Power Platform environments for dynamic dropdown
    /// </summary>
    private async Task<HttpResponseMessage> HandleListEnvironmentsAsync(string correlationId)
    {
        var url = $"{POWER_PLATFORM_API}/environmentmanagement/environments?api-version=2022-03-01-preview";
        
        this.Context.Logger.LogInformation($"[{correlationId}] Calling Power Platform API: GET environments");
        
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        CopyAuthHeader(request);
        request.Headers.Add("Accept", "application/json");
        
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogError($"[{correlationId}] Power Platform API error: {(int)response.StatusCode} - {content}");
            return CreateErrorResponse(response.StatusCode, $"Failed to list environments: {content}");
        }
        
        // Transform response to simplified format for dropdown
        var data = JObject.Parse(content);
        var environments = data["value"] as JArray ?? new JArray();
        
        this.Context.Logger.LogInformation($"[{correlationId}] Found {environments.Count} environments");
        
        var simplified = new JArray();
        foreach (var env in environments)
        {
            simplified.Add(new JObject
            {
                ["id"] = env["id"]?.ToString() ?? "",
                ["displayName"] = env["displayName"]?.ToString() ?? env["id"]?.ToString() ?? "Unknown",
                ["type"] = env["type"]?.ToString() ?? "",
                ["state"] = env["state"]?.ToString() ?? "",
                ["url"] = env["url"]?.ToString() ?? "",
                ["tenantId"] = env["tenantId"]?.ToString() ?? ""
            });
        }
        
        var result = new JObject { ["value"] = simplified };
        return CreateJsonResponse(result);
    }
    
    /// <summary>
    /// Get Copilot credits for an environment
    /// </summary>
    private async Task<HttpResponseMessage> HandleGetCreditsAsync(string correlationId)
    {
        // Parse query parameters
        var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
        var environmentId = query["environmentId"];
        var fromDate = query["fromDate"];
        var toDate = query["toDate"];
        
        this.Context.Logger.LogInformation($"[{correlationId}] GetCredits - Environment: {environmentId}, From: {fromDate}, To: {toDate}");
        
        if (string.IsNullOrWhiteSpace(environmentId))
        {
            this.Context.Logger.LogWarning($"[{correlationId}] Missing required parameter: environmentId");
            return CreateErrorResponse(HttpStatusCode.BadRequest, "environmentId is required");
        }
        if (string.IsNullOrWhiteSpace(fromDate) || string.IsNullOrWhiteSpace(toDate))
        {
            this.Context.Logger.LogWarning($"[{correlationId}] Missing required parameters: fromDate or toDate");
            return CreateErrorResponse(HttpStatusCode.BadRequest, "fromDate and toDate are required");
        }
        
        // Get tenant ID from the environments API first (or from token)
        this.Context.Logger.LogInformation($"[{correlationId}] Retrieving tenant ID from environments API");
        var tenantId = await GetTenantIdAsync(correlationId).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            this.Context.Logger.LogError($"[{correlationId}] Could not determine tenant ID");
            return CreateErrorResponse(HttpStatusCode.BadRequest, "Could not determine tenant ID");
        }
        
        this.Context.Logger.LogInformation($"[{correlationId}] Tenant ID: {tenantId}");
        
        // Convert dates from ISO format to MM-dd-yyyy format required by API
        var fromFormatted = FormatDateForApi(fromDate);
        var toFormatted = FormatDateForApi(toDate);
        
        var url = $"{LICENSING_API}/v0.1-alpha/tenants/{tenantId}/entitlements/MCSMessages/environments/{environmentId}/resources?fromDate={fromFormatted}&toDate={toFormatted}";
        
        this.Context.Logger.LogInformation($"[{correlationId}] Calling Licensing API: GET credits for environment {environmentId}");
        
        // Need to get a token for the licensing API
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        CopyAuthHeader(request);
        request.Headers.Add("Accept", "application/json");
        
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            // Return empty result for 404 (no data)
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                this.Context.Logger.LogInformation($"[{correlationId}] No credits data found for environment {environmentId}");
                return CreateJsonResponse(new JObject
                {
                    ["environmentId"] = environmentId,
                    ["tenantId"] = tenantId,
                    ["fromDate"] = fromDate,
                    ["toDate"] = toDate,
                    ["resourceCount"] = 0,
                    ["resources"] = new JArray(),
                    ["message"] = "No credits data found for this environment in the specified date range"
                });
            }
            this.Context.Logger.LogError($"[{correlationId}] Licensing API error: {(int)response.StatusCode} - {content}");
            return CreateErrorResponse(response.StatusCode, $"Failed to get credits: {content}");
        }
        
        // Transform response to flatten structure
        var data = JObject.Parse(content);
        var values = data["value"] as JArray ?? new JArray();
        
        var allResources = new JArray();
        foreach (var val in values)
        {
            var resources = val["resources"] as JArray ?? new JArray();
            foreach (var resource in resources)
            {
                var metadata = resource["metadata"] as JObject ?? new JObject();
                allResources.Add(new JObject
                {
                    ["resourceId"] = resource["resourceId"],
                    ["resourceName"] = metadata["ResourceName"],
                    ["productName"] = metadata["ProductName"],
                    ["featureName"] = metadata["FeatureName"],
                    ["channelId"] = metadata["ChannelId"],
                    ["billedCredits"] = resource["consumed"],
                    ["nonBilledCredits"] = metadata["NonBillableQuantity"],
                    ["unit"] = resource["unit"],
                    ["lastRefreshed"] = resource["lastRefreshedDate"]
                });
            }
        }
        
        this.Context.Logger.LogInformation($"[{correlationId}] Retrieved {allResources.Count} resources with credits data");
        
        // Calculate totals for logging
        var totalBilled = allResources.Sum(r => r["billedCredits"]?.Value<decimal>() ?? 0);
        var totalNonBilled = allResources.Sum(r => r["nonBilledCredits"]?.Value<decimal>() ?? 0);
        this.Context.Logger.LogInformation($"[{correlationId}] Totals - Billed: {totalBilled}, Non-Billed: {totalNonBilled}");
        
        var result = new JObject
        {
            ["environmentId"] = environmentId,
            ["tenantId"] = tenantId,
            ["fromDate"] = fromDate,
            ["toDate"] = toDate,
            ["resourceCount"] = allResources.Count,
            ["resources"] = allResources
        };
        
        return CreateJsonResponse(result);
    }
    
    /// <summary>
    /// Get tenant entitlements
    /// </summary>
    private async Task<HttpResponseMessage> HandleGetEntitlementsAsync(string correlationId)
    {
        this.Context.Logger.LogInformation($"[{correlationId}] Retrieving tenant ID for entitlements");
        
        var tenantId = await GetTenantIdAsync(correlationId).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            this.Context.Logger.LogError($"[{correlationId}] Could not determine tenant ID");
            return CreateErrorResponse(HttpStatusCode.BadRequest, "Could not determine tenant ID");
        }
        
        var url = $"{LICENSING_API}/v0.1-alpha/tenants/{tenantId}/entitlements";
        
        this.Context.Logger.LogInformation($"[{correlationId}] Calling Licensing API: GET entitlements for tenant {tenantId}");
        
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        CopyAuthHeader(request);
        request.Headers.Add("Accept", "application/json");
        
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogError($"[{correlationId}] Licensing API error: {(int)response.StatusCode} - {content}");
            return CreateErrorResponse(response.StatusCode, $"Failed to get entitlements: {content}");
        }
        
        var data = JObject.Parse(content);
        data["tenantId"] = tenantId;
        
        this.Context.Logger.LogInformation($"[{correlationId}] Successfully retrieved tenant entitlements");
        
        return CreateJsonResponse(data);
    }
    
    /// <summary>
    /// Get tenant ID by calling the environments API and extracting from first environment
    /// </summary>
    private async Task<string> GetTenantIdAsync(string correlationId)
    {
        var url = $"{POWER_PLATFORM_API}/environmentmanagement/environments?api-version=2022-03-01-preview&$top=1";
        
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        CopyAuthHeader(request);
        request.Headers.Add("Accept", "application/json");
        
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogWarning($"[{correlationId}] Failed to get tenant ID from environments API: {(int)response.StatusCode}");
            return null;
        }
        
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(content);
        var environments = data["value"] as JArray;
        
        if (environments != null && environments.Count > 0)
        {
            var tenantId = environments[0]["tenantId"]?.ToString();
            this.Context.Logger.LogInformation($"[{correlationId}] Extracted tenant ID from environment");
            return tenantId;
        }
        
        this.Context.Logger.LogWarning($"[{correlationId}] No environments found to extract tenant ID");
        return null;
    }
    
    /// <summary>
    /// Convert ISO date format to MM-dd-yyyy format required by licensing API
    /// </summary>
    private string FormatDateForApi(string dateStr)
    {
        if (DateTime.TryParse(dateStr, out var date))
        {
            return date.ToString("MM-dd-yyyy");
        }
        // If already in expected format or can't parse, return as-is
        return dateStr;
    }
    
    /// <summary>
    /// Copy authorization header from original request
    /// </summary>
    private void CopyAuthHeader(HttpRequestMessage request)
    {
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
    }
    
    /// <summary>
    /// Create a JSON response
    /// </summary>
    private HttpResponseMessage CreateJsonResponse(JToken data, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(data.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        return response;
    }
    
    /// <summary>
    /// Create an error response
    /// </summary>
    private HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string message)
    {
        var error = new JObject
        {
            ["error"] = new JObject
            {
                ["code"] = ((int)statusCode).ToString(),
                ["message"] = message
            }
        };
        return CreateJsonResponse(error, statusCode);
    }
}
