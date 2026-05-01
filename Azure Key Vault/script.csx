using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string API_VERSION = "2025-07-01";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var operationId = this.Context.OperationId ?? "";

        this.Context.Logger.LogInformation($"{operationId} started. CorrelationId: {correlationId}");

        try
        {
            var vaultUrl = $"{this.Context.Request.RequestUri.Scheme}://{this.Context.Request.RequestUri.Host}";
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var payload = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);

            JObject result;

            switch (operationId)
            {
                case "BulkGetSecrets":
                    result = await HandleBulkGetSecrets(vaultUrl, payload, correlationId).ConfigureAwait(false);
                    break;
                case "CheckExpiringSecrets":
                    result = await HandleCheckExpiringSecrets(vaultUrl, payload, correlationId).ConfigureAwait(false);
                    break;
                case "SearchSecretsByTags":
                    result = await HandleSearchSecretsByTags(vaultUrl, payload, correlationId).ConfigureAwait(false);
                    break;
                case "SecretRotationReport":
                    result = await HandleSecretRotationReport(vaultUrl, payload, correlationId).ConfigureAwait(false);
                    break;
                case "BulkSetSecrets":
                    result = await HandleBulkSetSecrets(vaultUrl, payload, correlationId).ConfigureAwait(false);
                    break;
                default:
                    return CreateErrorResponse($"Unknown operation: {operationId}", 400);
            }

            var duration = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"{operationId} completed. CorrelationId: {correlationId}, Duration: {duration.TotalMilliseconds}ms");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(result.ToString(), Encoding.UTF8, "application/json")
            };
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"CorrelationId: {correlationId}, Error: {ex.Message}");

            return CreateErrorResponse(ex.Message, 500);
        }
    }

    private async Task<JObject> HandleBulkGetSecrets(string vaultUrl, JObject payload, string correlationId)
    {
        var nameFilter = payload.Value<string>("nameFilter") ?? "";
        var maxSecrets = payload.Value<int?>("maxSecrets") ?? 25;

        var allSecrets = await ListAllSecrets(vaultUrl).ConfigureAwait(false);

        // Filter by name prefix
        var filtered = FilterByName(allSecrets, nameFilter, maxSecrets);

        // Fetch each secret value
        var results = new JArray();
        foreach (var secret in filtered)
        {
            var secretName = ExtractSecretName(secret);
            try
            {
                var getUrl = $"{vaultUrl}/secrets/{Uri.EscapeDataString(secretName)}?api-version={API_VERSION}";
                var secretData = await SendVaultRequest(HttpMethod.Get, getUrl).ConfigureAwait(false);
                results.Add(new JObject
                {
                    ["name"] = secretName,
                    ["value"] = secretData.Value<string>("value"),
                    ["contentType"] = secretData.Value<string>("contentType"),
                    ["enabled"] = secretData["attributes"]?.Value<bool?>("enabled")
                });
            }
            catch (Exception ex)
            {
                this.Context.Logger.LogWarning($"Failed to get secret '{secretName}': {ex.Message}");
                results.Add(new JObject
                {
                    ["name"] = secretName,
                    ["error"] = ex.Message
                });
            }
        }

        // TODO: Use retrieved secrets here before returning, e.g.:
        // - Configure downstream API clients with connection strings
        // - Build a settings dictionary for environment bootstrapping
        // - Pass secrets to another service via SendVaultRequest or Context.SendAsync
        //
        // Example:
        // foreach (var r in results.Where(r => r["error"] == null))
        // {
        //     var name = r.Value<string>("name");
        //     var value = r.Value<string>("value");
        //     // Use name/value to configure a client, set headers, etc.
        // }

        return new JObject
        {
            ["count"] = results.Count,
            ["secrets"] = results
        };
    }

    private async Task<JObject> HandleCheckExpiringSecrets(string vaultUrl, JObject payload, string correlationId)
    {
        var days = payload.Value<int?>("daysUntilExpiry") ?? 30;
        var nameFilter = payload.Value<string>("nameFilter") ?? "";
        var cutoff = DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var allSecrets = await ListAllSecrets(vaultUrl).ConfigureAwait(false);
        var filtered = FilterByName(allSecrets, nameFilter, 25);

        // Fetch each secret to check expiry
        var expiring = new JArray();
        var alreadyExpired = new JArray();

        foreach (var secret in filtered)
        {
            var secretName = ExtractSecretName(secret);
            try
            {
                var getUrl = $"{vaultUrl}/secrets/{Uri.EscapeDataString(secretName)}?api-version={API_VERSION}";
                var secretData = await SendVaultRequest(HttpMethod.Get, getUrl).ConfigureAwait(false);
                var exp = secretData["attributes"]?.Value<long?>("exp");

                if (exp == null) continue; // No expiry set

                var expiryDate = DateTimeOffset.FromUnixTimeSeconds(exp.Value).UtcDateTime;

                if (exp.Value < now)
                {
                    alreadyExpired.Add(new JObject
                    {
                        ["name"] = secretName,
                        ["expiryDate"] = expiryDate.ToString("o"),
                        ["status"] = "expired",
                        ["enabled"] = secretData["attributes"]?.Value<bool?>("enabled")
                    });
                }
                else if (exp.Value <= cutoff)
                {
                    var daysRemaining = (int)Math.Ceiling((expiryDate - DateTime.UtcNow).TotalDays);
                    expiring.Add(new JObject
                    {
                        ["name"] = secretName,
                        ["expiryDate"] = expiryDate.ToString("o"),
                        ["daysRemaining"] = daysRemaining,
                        ["status"] = "expiring",
                        ["enabled"] = secretData["attributes"]?.Value<bool?>("enabled")
                    });
                }
            }
            catch (Exception ex)
            {
                this.Context.Logger.LogWarning($"Failed to check secret '{secretName}': {ex.Message}");
            }
        }

        // TODO: Act on expiring/expired secrets here before returning, e.g.:
        // - Trigger rotation by calling a key rotation API or Azure Function
        // - Send an alert via email, Teams, or a notification service
        // - Disable expired secrets automatically via Key Vault Update Secret
        //
        // Example:
        // foreach (var exp in alreadyExpired)
        // {
        //     var name = exp.Value<string>("name");
        //     // Call your rotation endpoint or send a notification
        // }

        return new JObject
        {
            ["daysChecked"] = days,
            ["expiringCount"] = expiring.Count,
            ["expiredCount"] = alreadyExpired.Count,
            ["expiring"] = expiring,
            ["expired"] = alreadyExpired
        };
    }

    private async Task<JObject> HandleSearchSecretsByTags(string vaultUrl, JObject payload, string correlationId)
    {
        var tagKey = payload.Value<string>("tagKey") ?? "";
        var tagValue = payload.Value<string>("tagValue");
        var includeValues = payload.Value<bool?>("includeValues") ?? false;

        if (string.IsNullOrWhiteSpace(tagKey))
            throw new ArgumentException("'tagKey' is required");

        var allSecrets = await ListAllSecrets(vaultUrl).ConfigureAwait(false);

        // Fetch each secret to check tags
        var matches = new JArray();
        foreach (var secret in allSecrets)
        {
            var secretName = ExtractSecretName(secret);
            try
            {
                var getUrl = $"{vaultUrl}/secrets/{Uri.EscapeDataString(secretName)}?api-version={API_VERSION}";
                var secretData = await SendVaultRequest(HttpMethod.Get, getUrl).ConfigureAwait(false);
                var tags = secretData["tags"] as JObject;

                if (tags == null) continue;

                var actualValue = tags.Value<string>(tagKey);
                if (actualValue == null) continue;

                // If tagValue specified, must match
                if (!string.IsNullOrWhiteSpace(tagValue) &&
                    !string.Equals(actualValue, tagValue, StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = new JObject
                {
                    ["name"] = secretName,
                    ["tags"] = tags,
                    ["contentType"] = secretData.Value<string>("contentType"),
                    ["enabled"] = secretData["attributes"]?.Value<bool?>("enabled")
                };

                if (includeValues)
                    match["value"] = secretData.Value<string>("value");

                matches.Add(match);
            }
            catch (Exception ex)
            {
                this.Context.Logger.LogWarning($"Failed to check tags for secret '{secretName}': {ex.Message}");
            }
        }

        // TODO: Use tag-filtered secrets here before returning, e.g.:
        // - Pull all "environment=production" secrets for a deployment pipeline
        // - Group secrets by team or service tag for audit reporting
        // - Sync tagged secrets to another vault or config store
        //
        // Example:
        // var prodSecrets = matches.Where(m => m["tags"]?["environment"]?.ToString() == "production");
        // foreach (var s in prodSecrets)
        // {
        //     // Push to deployment config, populate env vars, etc.
        // }

        return new JObject
        {
            ["tagKey"] = tagKey,
            ["tagValue"] = tagValue ?? "(any)",
            ["count"] = matches.Count,
            ["secrets"] = matches
        };
    }

    private async Task<JObject> HandleSecretRotationReport(string vaultUrl, JObject payload, string correlationId)
    {
        var staleDays = payload.Value<int?>("staleDays") ?? 90;
        var nameFilter = payload.Value<string>("nameFilter") ?? "";
        var cutoff = DateTimeOffset.UtcNow.AddDays(-staleDays).ToUnixTimeSeconds();

        var allSecrets = await ListAllSecrets(vaultUrl).ConfigureAwait(false);
        var filtered = FilterByName(allSecrets, nameFilter, 25);

        var stale = new JArray();
        var healthy = 0;

        foreach (var secret in filtered)
        {
            var secretName = ExtractSecretName(secret);
            try
            {
                var getUrl = $"{vaultUrl}/secrets/{Uri.EscapeDataString(secretName)}?api-version={API_VERSION}";
                var secretData = await SendVaultRequest(HttpMethod.Get, getUrl).ConfigureAwait(false);
                var updated = secretData["attributes"]?.Value<long?>("updated");
                var created = secretData["attributes"]?.Value<long?>("created");
                var lastModified = updated ?? created ?? 0;

                if (lastModified > 0 && lastModified < cutoff)
                {
                    var lastModifiedDate = DateTimeOffset.FromUnixTimeSeconds(lastModified).UtcDateTime;
                    var daysSinceUpdate = (int)Math.Floor((DateTime.UtcNow - lastModifiedDate).TotalDays);

                    stale.Add(new JObject
                    {
                        ["name"] = secretName,
                        ["lastUpdated"] = lastModifiedDate.ToString("o"),
                        ["daysSinceUpdate"] = daysSinceUpdate,
                        ["enabled"] = secretData["attributes"]?.Value<bool?>("enabled"),
                        ["contentType"] = secretData.Value<string>("contentType")
                    });
                }
                else
                {
                    healthy++;
                }
            }
            catch (Exception ex)
            {
                this.Context.Logger.LogWarning($"Failed to check rotation for secret '{secretName}': {ex.Message}");
            }
        }

        // Sort by oldest first
        var sortedStale = new JArray(stale.OrderByDescending(s => s.Value<int>("daysSinceUpdate")));

        // TODO: Act on stale secrets here before returning, e.g.:
        // - Generate new values and rotate via BulkSetSecrets
        // - Create a compliance report for security review
        // - File tickets in Jira/ServiceNow for secret owners
        //
        // Example:
        // foreach (var s in sortedStale)
        // {
        //     var name = s.Value<string>("name");
        //     var age = s.Value<int>("daysSinceUpdate");
        //     // Queue rotation or create a work item
        // }

        return new JObject
        {
            ["staleDays"] = staleDays,
            ["staleCount"] = sortedStale.Count,
            ["healthyCount"] = healthy,
            ["staleSecrets"] = sortedStale
        };
    }

    private async Task<JObject> HandleBulkSetSecrets(string vaultUrl, JObject payload, string correlationId)
    {
        var secrets = payload["secrets"] as JArray;
        if (secrets == null || secrets.Count == 0)
            throw new ArgumentException("'secrets' array is required and must contain at least one entry");

        if (secrets.Count > 25)
            throw new ArgumentException("Maximum 25 secrets per bulk set operation");

        var results = new JArray();
        var successCount = 0;
        var errorCount = 0;

        foreach (var secret in secrets)
        {
            var secretName = secret.Value<string>("name") ?? "";
            var secretValue = secret.Value<string>("value") ?? "";

            if (string.IsNullOrWhiteSpace(secretName))
            {
                errorCount++;
                results.Add(new JObject
                {
                    ["name"] = "(empty)",
                    ["success"] = false,
                    ["error"] = "'name' is required for each secret"
                });
                continue;
            }

            try
            {
                var setUrl = $"{vaultUrl}/secrets/{Uri.EscapeDataString(secretName)}?api-version={API_VERSION}";
                var setBody = new JObject { ["value"] = secretValue };

                var contentType = secret.Value<string>("contentType");
                if (!string.IsNullOrWhiteSpace(contentType))
                    setBody["contentType"] = contentType;

                var tags = secret["tags"] as JObject;
                if (tags != null)
                    setBody["tags"] = tags;

                var setResult = await SendVaultRequest(new HttpMethod("PUT"), setUrl, setBody).ConfigureAwait(false);
                successCount++;

                results.Add(new JObject
                {
                    ["name"] = secretName,
                    ["success"] = true,
                    ["id"] = setResult.Value<string>("id")
                });
            }
            catch (Exception ex)
            {
                errorCount++;
                this.Context.Logger.LogWarning($"Failed to set secret '{secretName}': {ex.Message}");
                results.Add(new JObject
                {
                    ["name"] = secretName,
                    ["success"] = false,
                    ["error"] = ex.Message
                });
            }
        }

        // TODO: Post-processing after bulk set, e.g.:
        // - Verify created secrets by reading them back
        // - Update a config database with the new secret versions
        // - Notify downstream services that secrets have been rotated
        //
        // Example:
        // var created = results.Where(r => r.Value<bool>("success"));
        // foreach (var c in created)
        // {
        //     var id = c.Value<string>("id");
        //     // Log the new version ID or trigger a dependent deployment
        // }

        return new JObject
        {
            ["successCount"] = successCount,
            ["errorCount"] = errorCount,
            ["results"] = results
        };
    }

    private async Task<List<JToken>> ListAllSecrets(string vaultUrl)
    {
        var allItems = new List<JToken>();
        var url = $"{vaultUrl}/secrets?api-version={API_VERSION}&maxresults=25";

        while (!string.IsNullOrWhiteSpace(url))
        {
            var listResult = await SendVaultRequest(HttpMethod.Get, url).ConfigureAwait(false);
            var items = listResult["value"] as JArray;
            if (items != null)
                allItems.AddRange(items);

            url = listResult.Value<string>("nextLink");
        }

        return allItems;
    }

    private static string ExtractSecretName(JToken secretItem)
    {
        var id = secretItem.Value<string>("id") ?? "";
        return id.Split('/').LastOrDefault() ?? "";
    }

    private static List<JToken> FilterByName(List<JToken> secrets, string nameFilter, int max)
    {
        var filtered = new List<JToken>();
        foreach (var item in secrets)
        {
            var name = ExtractSecretName(item);
            if (string.IsNullOrWhiteSpace(nameFilter) ||
                name.StartsWith(nameFilter, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(item);
            }
            if (filtered.Count >= max) break;
        }
        return filtered;
    }

    private async Task<JObject> SendVaultRequest(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Key Vault request failed ({(int)response.StatusCode}): {content}");

        return JObject.Parse(content);
    }

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
