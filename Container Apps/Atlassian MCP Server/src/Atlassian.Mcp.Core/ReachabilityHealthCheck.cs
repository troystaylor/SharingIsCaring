using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Atlassian.Mcp.Core;

/// <summary>
/// Reports whether a provider's API answers at all, cached.
/// </summary>
/// <remarks>
/// The server holds no credential of its own, so the probe sends none. Any answer below 500, including a
/// 401, shows the provider is up. It is cached because readiness probes fire every few seconds on every
/// replica. A failure reports Degraded rather than Unhealthy, so a provider outage does not make Container
/// Apps recycle a process that is itself fine.
/// </remarks>
public sealed class ReachabilityHealthCheck(
    IHttpClientFactory httpClientFactory,
    TimeProvider time,
    ILogger<ReachabilityHealthCheck> logger,
    string provider,
    string clientName,
    string path) : IHealthCheck
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
                    var http = httpClientFactory.CreateClient(clientName);
                    using var response = await http.GetAsync(path, cancellationToken);
                    _reachable = (int)response.StatusCode < 500;

                    if (!_reachable)
                    {
                        logger.LogWarning("{Provider} reachability check returned HTTP {Status}", provider, (int)response.StatusCode);
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _reachable = false;
                    logger.LogWarning("{Provider} reachability check failed: {Reason}", provider, ex.GetType().Name);
                }

                _checkedAt = time.GetUtcNow();
            }

            return _reachable
                ? HealthCheckResult.Healthy($"{provider} is reachable")
                : HealthCheckResult.Degraded($"{provider} is unreachable; the server is up and will keep checking");
        }
        finally
        {
            _gate.Release();
        }
    }
}
