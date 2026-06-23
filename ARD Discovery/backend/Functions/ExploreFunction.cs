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
/// POST /explore — ARD explore endpoint (§7.3).
/// Returns facet aggregations from the local CatalogIndex.
/// Does not federate (per spec: "Explore does not federate").
/// </summary>
public class ExploreFunction
{
    private readonly CatalogIndex _index;

    public ExploreFunction(CatalogIndex index)
    {
        _index = index;
    }

    [Function("Explore")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "explore")] HttpRequestData req)
    {
        if (!AuthHelper.ValidateApiKey(req))
            return AuthHelper.Unauthorized(req);

        var bodyString = await req.ReadAsStringAsync();
        var body = JsonNode.Parse(bodyString ?? "{}");

        var text = body?["query"]?["text"]?.GetValue<string>();
        var facetsNode = body?["resultType"]?["facets"]?.AsArray();

        var fields = facetsNode?
            .Select(f => f?["field"]?.GetValue<string>())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToArray() ?? new[] { "type" };

        var limit = facetsNode?.FirstOrDefault()?["limit"]?.GetValue<int>() ?? 20;

        var facets = _index.Explore(fields!, text, limit);

        // Build response per ARD spec §7.3
        var facetsObj = new JsonObject();
        foreach (var (field, buckets) in facets)
        {
            var bucketsArray = new JsonArray();
            foreach (var (value, count) in buckets)
            {
                bucketsArray.Add(new JsonObject
                {
                    ["value"] = value,
                    ["count"] = count
                });
            }
            facetsObj[field] = new JsonObject { ["buckets"] = bucketsArray };
        }

        var responseBody = new JsonObject
        {
            ["resultType"] = "facets",
            ["facets"] = facetsObj
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        await response.WriteStringAsync(responseBody.ToJsonString());
        return response;
    }
}
