using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mongo.Copilot.Indexer;

/// <summary>
/// Tracks that the indexer is making progress, so a wedged change stream is visible to the
/// container platform rather than only in the logs.
/// </summary>
/// <remarks>
/// The indexer runs without ingress and never exits on its own, so without a heartbeat a
/// container whose tail has silently stopped looks identical to a healthy idle one and is
/// never restarted.
/// </remarks>
public sealed class IndexerHeartbeat
{
    private long _lastBeatTicks = DateTimeOffset.UtcNow.UtcTicks;
    private volatile bool _started;
    private volatile string _phase = "starting";

    /// <summary>
    /// How long the indexer may go without a beat before it is considered unhealthy. A change
    /// stream that sees no events still beats each time its cursor poll returns empty, so this
    /// only needs to exceed the driver's await time.
    /// </summary>
    public TimeSpan Tolerance { get; init; } = TimeSpan.FromMinutes(10);

    public string Phase => _phase;

    public DateTimeOffset LastBeatUtc => new(Interlocked.Read(ref _lastBeatTicks), TimeSpan.Zero);

    public void Beat(string phase)
    {
        _phase = phase;
        _started = true;
        Interlocked.Exchange(ref _lastBeatTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    public bool IsHealthy => !_started || DateTimeOffset.UtcNow - LastBeatUtc <= Tolerance;
}

public sealed class IndexerHealthCheck(IndexerHeartbeat heartbeat) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var age = DateTimeOffset.UtcNow - heartbeat.LastBeatUtc;

        var data = new Dictionary<string, object>
        {
            ["phase"] = heartbeat.Phase,
            ["lastBeatUtc"] = heartbeat.LastBeatUtc,
            ["secondsSinceLastBeat"] = (int)age.TotalSeconds
        };

        return Task.FromResult(heartbeat.IsHealthy
            ? HealthCheckResult.Healthy($"Indexer active ({heartbeat.Phase}).", data)
            : HealthCheckResult.Unhealthy(
                $"No indexer progress for {age.TotalMinutes:F1} minutes (phase '{heartbeat.Phase}').", data: data));
    }
}
