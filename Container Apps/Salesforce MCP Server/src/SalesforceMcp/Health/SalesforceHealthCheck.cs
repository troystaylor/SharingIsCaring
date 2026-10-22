using Microsoft.Extensions.Diagnostics.HealthChecks;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Health;

/// <summary>
/// Reports whether Salesforce is reachable, cached.
/// </summary>
/// <remarks>
/// The probe calls the public version list, which needs no sign-in, because the server holds no Salesforce
/// credential of its own to test. It is cached because readiness probes fire every few seconds on every
/// replica. A failure reports Degraded rather than Unhealthy, so a Salesforce outage does not make Container
/// Apps recycle a process that is itself fine.
/// </remarks>
public sealed class SalesforceHealthCheck(
    IHttpClientFactory httpClientFactory,
    TimeProvider time,
    ILogger<SalesforceHealthCheck> logger) : IHealthCheck
{
    private static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(45);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;
    private bool _reachable;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (time.GetUtcNow() - _checkedAt >= CacheWindow)
            {
                try
                {
                    var http = httpClientFactory.CreateClient(SalesforceHttp.PublicClientName);
                    using var response = await http.GetAsync("services/data/", cancellationToken);
                    _reachable = response.IsSuccessStatusCode;

                    if (!_reachable)
                    {
                        logger.LogWarning("Salesforce reachability check returned HTTP {Status}", (int)response.StatusCode);
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _reachable = false;
                    logger.LogWarning("Salesforce reachability check failed: {Reason}", ex.GetType().Name);
                }

                _checkedAt = time.GetUtcNow();
            }

            return _reachable
                ? HealthCheckResult.Healthy("Salesforce is reachable")
                : HealthCheckResult.Degraded("Salesforce is unreachable; the server is up and will keep checking");
        }
        finally
        {
            _gate.Release();
        }
    }
}
