#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArdDiscovery.Services;

namespace ArdDiscovery.Functions;

/// <summary>
/// Health check and operational endpoints:
/// - GET /health: liveness/readiness probe
/// - POST /crawl-now: manual crawl trigger (requires API key)
/// - GET /robots.txt: Agentmap for ARD-aware crawlers
/// </summary>
public class OperationalFunctions
{
    private readonly CatalogIndex _index;
    private readonly TrustVerifier _trust;
    private readonly ILogger<OperationalFunctions> _logger;

    public OperationalFunctions(CatalogIndex index, TrustVerifier trust, ILogger<OperationalFunctions> logger)
    {
        _index = index;
        _trust = trust;
        _logger = logger;
    }

    /// <summary>
    /// Liveness/readiness probe. Returns 200 with index stats.
    /// </summary>
    [Function("Health")]
    public async Task<HttpResponseData> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);

        int indexCount = -1;
        try { indexCount = _index.Count; } catch { /* table not ready */ }

        var body = new
        {
            status = "healthy",
            indexCount,
            timestamp = DateTimeOffset.UtcNow
        };

        await response.WriteAsJsonAsync(body);
        return response;
    }

    /// <summary>
    /// Manual crawl trigger. Requires x-api-key header matching BackendApiKey.
    /// Optionally accepts ?domains=a.com,b.com to override CrawlDomains.
    /// </summary>
    [Function("CrawlNow")]
    public async Task<HttpResponseData> CrawlNow(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "crawl-now")] HttpRequestData req)
    {
        // Authenticate
        var expectedKey = Environment.GetEnvironmentVariable("BackendApiKey") ?? string.Empty;
        var providedKey = req.Headers.Contains("x-api-key")
            ? req.Headers.GetValues("x-api-key").FirstOrDefault() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(expectedKey) || !string.Equals(expectedKey, providedKey, StringComparison.Ordinal))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Invalid or missing x-api-key" });
            return unauthorized;
        }

        // Determine domains to crawl
        var domainsParam = req.Url.Query?.Contains("domains=") == true
            ? System.Web.HttpUtility.ParseQueryString(req.Url.Query)["domains"]
            : null;
        var domainsConfig = domainsParam ?? Environment.GetEnvironmentVariable("CrawlDomains") ?? string.Empty;
        var domains = domainsConfig
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToArray();

        if (domains.Length == 0)
        {
            var noContent = req.CreateResponse(HttpStatusCode.BadRequest);
            await noContent.WriteAsJsonAsync(new { error = "No domains specified (set CrawlDomains or pass ?domains=)" });
            return noContent;
        }

        _logger.LogInformation("Manual crawl triggered for {Count} domains", domains.Length);

        foreach (var domain in domains)
        {
            var trustResult = await _trust.VerifyAsync(domain);
            _logger.LogInformation("Trust for {Domain}: score={Score}, level={Level}",
                domain, trustResult.Score, trustResult.Level);
            await _index.CrawlAsync(domain);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            crawled = domains,
            indexCount = _index.Count,
            timestamp = DateTimeOffset.UtcNow
        });
        return response;
    }

    /// <summary>
    /// Robots.txt with Agentmap directive per ARD spec §6.4.
    /// Tells ARD-aware crawlers where to find the ai-catalog.json.
    /// </summary>
    [Function("RobotsTxt")]
    public HttpResponseData RobotsTxt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "robots.txt")] HttpRequestData req)
    {
        var publisherDomain = Environment.GetEnvironmentVariable("PublisherDomain")
            ?? Environment.GetEnvironmentVariable("BackendBaseUrl")?.Replace("https://", "")
            ?? "localhost";

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain");

        var body = $"""
            User-agent: *
            Allow: /

            # ARD Agentmap - Agentic Resource Discovery
            # See: https://github.com/nicholasgoulding/ard
            Agentmap: https://{publisherDomain}/.well-known/ai-catalog.json
            """;

        response.WriteString(body);
        return response;
    }
}
