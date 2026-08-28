using Microsoft.Extensions.Diagnostics.HealthChecks;
using Mongo.Copilot.Indexer;

namespace Mongo.Copilot.Indexer.Tests;

public class IndexerHeartbeatTests
{
    [Fact]
    public void A_heartbeat_that_has_never_beaten_is_healthy()
    {
        // Startup performs a full crawl before the first beat, so an unbeaten heartbeat must
        // not fail the liveness probe and restart the container mid-crawl.
        Assert.True(new IndexerHeartbeat().IsHealthy);
    }

    [Fact]
    public void A_recent_beat_is_healthy()
    {
        var heartbeat = new IndexerHeartbeat();
        heartbeat.Beat("tailing tickets");

        Assert.True(heartbeat.IsHealthy);
        Assert.Equal("tailing tickets", heartbeat.Phase);
    }

    [Fact]
    public async Task A_stale_beat_is_unhealthy()
    {
        var heartbeat = new IndexerHeartbeat { Tolerance = TimeSpan.FromMilliseconds(50) };
        heartbeat.Beat("tailing tickets");

        await Task.Delay(120);

        Assert.False(heartbeat.IsHealthy);
    }

    [Fact]
    public async Task The_health_check_reports_the_stalled_phase()
    {
        var heartbeat = new IndexerHeartbeat { Tolerance = TimeSpan.FromMilliseconds(50) };
        heartbeat.Beat("tailing orders");
        await Task.Delay(120);

        var result = await new IndexerHealthCheck(heartbeat)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("tailing orders", result.Data["phase"]);
    }

    [Fact]
    public async Task The_health_check_is_healthy_while_beats_continue()
    {
        var heartbeat = new IndexerHeartbeat();
        heartbeat.Beat("crawling tickets");

        var result = await new IndexerHealthCheck(heartbeat)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
