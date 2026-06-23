#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArdDiscovery.Services;

namespace ArdDiscovery.Functions;

/// <summary>
/// POST /search — ARD search endpoint (§7.2).
/// 
/// Merges results from the local CatalogIndex with results from the
/// configured upstream registry. Federation mode controls behavior:
///   - "none": local index only
///   - "auto": merge local + upstream results
///   - "referrals": local results + upstream registry as a referral
/// </summary>
public class SearchFunction
{
    private readonly RegistryClient _registry;
    private readonly CatalogIndex _index;
    private readonly ILogger<SearchFunction> _logger;

    public SearchFunction(RegistryClient registry, CatalogIndex index, ILogger<SearchFunction> logger)
    {
        _registry = registry;
        _index = index;
        _logger = logger;
    }

    [Function("Search")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "search")] HttpRequestData req)
    {
        if (!AuthHelper.ValidateApiKey(req))
            return AuthHelper.Unauthorized(req);

        var bodyString = await req.ReadAsStringAsync();
        var body = JsonNode.Parse(bodyString ?? "{}");

        var queryText = body?["query"]?["text"]?.GetValue<string>() ?? string.Empty;
        var federation = body?["federation"]?.GetValue<string>() ?? "auto";
        var pageSize = body?["pageSize"]?.GetValue<int>() ?? 10;

        // Parse filters
        Dictionary<string, string[]>? filters = null;
        var filterNode = body?["query"]?["filter"];
        if (filterNode != null)
        {
            filters = new Dictionary<string, string[]>();
            foreach (var prop in filterNode.AsObject())
            {
                if (prop.Value is JsonArray arr)
                {
                    filters[prop.Key] = arr
                        .Select(n => n?.GetValue<string>())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray()!;
                }
                else if (prop.Value != null)
                {
                    filters[prop.Key] = new[] { prop.Value.GetValue<string>() };
                }
            }
        }

        // Search local index
        var localResults = _index.Search(queryText, filters, pageSize);

        // Convert local results to ARD response format
        var results = new JsonArray();
        foreach (var entry in localResults)
        {
            var resultEntry = new JsonObject
            {
                ["identifier"] = entry.Identifier,
                ["displayName"] = entry.DisplayName,
                ["type"] = entry.Type,
                ["description"] = entry.Description,
                ["score"] = entry.ScoreAgainst(queryText),
                ["source"] = entry.Source,
                ["publisher"] = entry.Publisher,
                ["trustScore"] = entry.TrustScore
            };

            if (!string.IsNullOrEmpty(entry.Url))
                resultEntry["url"] = entry.Url;
            if (entry.Capabilities.Length > 0)
                resultEntry["capabilities"] = new JsonArray(entry.Capabilities.Select(c => JsonValue.Create(c)).ToArray());
            if (entry.Tags.Length > 0)
                resultEntry["tags"] = new JsonArray(entry.Tags.Select(t => JsonValue.Create(t)).ToArray());

            results.Add(resultEntry);
        }

        var responseBody = new JsonObject { ["results"] = results };

        // Federation
        var registryUrl = Environment.GetEnvironmentVariable("DefaultRegistryUrl");

        if (federation != "none" && !string.IsNullOrEmpty(registryUrl))
        {
            if (federation == "auto")
            {
                // Merge upstream results
                try
                {
                    var upstreamResult = await _registry.SearchAsync(registryUrl, body!);
                    var upstreamResults = upstreamResult?["results"]?.AsArray();
                    if (upstreamResults != null)
                    {
                        // Deduplicate by identifier
                        var existingIds = new HashSet<string>(
                            localResults.Select(e => e.Identifier));

                        foreach (var ur in upstreamResults)
                        {
                            var id = ur?["identifier"]?.GetValue<string>();
                            if (id != null && !existingIds.Contains(id))
                            {
                                results.Add(ur!.DeepClone());
                                existingIds.Add(id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query upstream registry {Url}", registryUrl);
                }
            }
            else if (federation == "referrals")
            {
                // Return upstream registry as a referral
                responseBody["referrals"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["identifier"] = $"urn:air:{new Uri(registryUrl).Host}:registry:upstream",
                        ["displayName"] = "Upstream Registry",
                        ["type"] = "application/ai-registry+json",
                        ["url"] = registryUrl
                    }
                };
            }
        }

        // Re-sort merged results by score descending
        var sorted = results
            .OrderByDescending(r => r?["score"]?.GetValue<int>() ?? 0)
            .Take(pageSize)
            .ToList();

        responseBody["results"] = new JsonArray(sorted.Select(s => s!.DeepClone()).ToArray());

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        await response.WriteStringAsync(responseBody.ToJsonString());
        return response;
    }
}
