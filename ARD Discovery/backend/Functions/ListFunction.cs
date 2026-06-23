#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ArdDiscovery.Services;

namespace ArdDiscovery.Functions;

/// <summary>
/// GET /agents — ARD list endpoint (§7.4).
/// Deterministic browsing from the local CatalogIndex.
/// </summary>
public class ListFunction
{
    private readonly CatalogIndex _index;

    public ListFunction(CatalogIndex index)
    {
        _index = index;
    }

    [Function("List")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agents")] HttpRequestData req)
    {
        if (!AuthHelper.ValidateApiKey(req))
            return AuthHelper.Unauthorized(req);

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var filter = query["filter"];
        var pageSize = int.TryParse(query["pageSize"], out var ps) ? Math.Min(ps, 100) : 20;
        var offset = int.TryParse(query["pageToken"], out var pt) ? pt : 0;

        var entries = _index.List(filter, pageSize, offset);

        var results = new JsonArray();
        foreach (var entry in entries)
        {
            var resultEntry = new JsonObject
            {
                ["identifier"] = entry.Identifier,
                ["displayName"] = entry.DisplayName,
                ["type"] = entry.Type,
                ["description"] = entry.Description,
                ["publisher"] = entry.Publisher,
                ["trustScore"] = entry.TrustScore
            };

            if (!string.IsNullOrEmpty(entry.Url))
                resultEntry["url"] = entry.Url;
            if (entry.Capabilities.Length > 0)
                resultEntry["capabilities"] = new JsonArray(
                    entry.Capabilities.Select(c => JsonValue.Create(c)).ToArray());
            if (entry.Tags.Length > 0)
                resultEntry["tags"] = new JsonArray(
                    entry.Tags.Select(t => JsonValue.Create(t)).ToArray());
            if (!string.IsNullOrEmpty(entry.Version))
                resultEntry["version"] = entry.Version;

            results.Add(resultEntry);
        }

        var responseBody = new JsonObject { ["results"] = results };

        // Pagination: if there are more results, provide a page token
        if (entries.Count == pageSize)
        {
            responseBody["pageToken"] = (offset + pageSize).ToString();
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        await response.WriteStringAsync(responseBody.ToJsonString());
        return response;
    }
}
