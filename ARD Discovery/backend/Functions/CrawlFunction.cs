#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ArdDiscovery.Services;

namespace ArdDiscovery.Functions;

/// <summary>
/// Timer-triggered function that crawls registered domains' ai-catalog.json
/// and indexes entries into the local CatalogIndex.
/// 
/// Per ARD spec §6.2: "Web Ingestion (Required): Crawling ai-catalog.json
/// files from discovered URIs. All ARD implementations MUST support this."
/// 
/// Configured domains via "CrawlDomains" app setting (comma-separated).
/// Runs every 6 hours by default.
/// </summary>
public class CrawlFunction
{
    private readonly CatalogIndex _index;
    private readonly TrustVerifier _trust;
    private readonly CrawlStateStore _crawlState;
    private readonly ILogger<CrawlFunction> _logger;

    // Skip domains crawled within the last 5 hours (timer fires every 6)
    private static readonly TimeSpan SkipWindow = TimeSpan.FromHours(5);

    public CrawlFunction(CatalogIndex index, TrustVerifier trust, CrawlStateStore crawlState,
        ILogger<CrawlFunction> logger)
    {
        _index = index;
        _trust = trust;
        _crawlState = crawlState;
        _logger = logger;
    }

    [Function("Crawl")]
    public async Task Run(
        [TimerTrigger("0 0 */6 * * *")] TimerInfo timer)
    {
        var domainsConfig = Environment.GetEnvironmentVariable("CrawlDomains") ?? string.Empty;
        var domains = domainsConfig
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToArray();

        if (domains.Length == 0)
        {
            _logger.LogInformation("No CrawlDomains configured, skipping crawl");
            return;
        }

        _logger.LogInformation("Starting crawl of {Count} domains", domains.Length);

        foreach (var domain in domains)
        {
            // Skip recently crawled domains
            if (await _crawlState.WasCrawledRecentlyAsync(domain, SkipWindow))
            {
                _logger.LogInformation("Skipping {Domain} — crawled recently", domain);
                continue;
            }

            try
            {
                // Verify trust before indexing
                var trustResult = await _trust.VerifyAsync(domain);
                _logger.LogInformation("Trust for {Domain}: score={Score}, level={Level}, signals=[{Signals}]",
                    domain, trustResult.Score, trustResult.Level, string.Join(", ", trustResult.Signals));

                if (trustResult.Warnings.Count > 0)
                {
                    _logger.LogWarning("Trust warnings for {Domain}: [{Warnings}]",
                        domain, string.Join(", ", trustResult.Warnings));
                }

                var countBefore = _index.Count;
                await _index.CrawlAsync(domain, trustResult.Score);
                var indexed = _index.Count - countBefore;

                await _crawlState.RecordSuccessAsync(domain, indexed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Crawl failed for {Domain}", domain);
                await _crawlState.RecordErrorAsync(domain, ex.Message);
            }
        }

        _logger.LogInformation("Crawl complete. Index now has {Count} entries", _index.Count);
    }
}
