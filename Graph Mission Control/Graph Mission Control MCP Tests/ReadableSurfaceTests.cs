using GraphMissionControl.Mcp.Graph;

namespace GraphMissionControl.Mcp.Tests;

/// <summary>
/// Federated connectors expose only tools — no resources, no prompts — so the catalog returned
/// on a rejected path is the only way an agent learns what it may read. If it drifts from the
/// index, most of the surface becomes unreachable again.
/// </summary>
public class ReadableSurfaceTests
{
    private static readonly CapabilityIndex Index = CapabilityIndex.LoadEmbedded();

    [Fact]
    public void SurfaceListsEveryReadableEndpoint()
    {
        var surface = Index.DescribeReadableSurface();

        Assert.All(Index.ReadOnlyOperations, op => Assert.Contains(op.Endpoint, surface, StringComparison.Ordinal));
    }

    [Fact]
    public void SurfaceIsGroupedUnderEveryDomain()
    {
        var surface = Index.DescribeReadableSurface();

        Assert.NotEmpty(Index.Domains);
        Assert.All(Index.Domains, domain => Assert.Contains(domain + ":", surface, StringComparison.Ordinal));
    }

    /// <summary>The whole point of the index: no write endpoint may be advertised as readable.</summary>
    [Fact]
    public void SurfaceAdvertisesNothingThatIsNotReadable()
    {
        var surface = Index.DescribeReadableSurface();
        var readable = Index.ReadOnlyOperations.Select(op => op.Endpoint).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var advertised = surface
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..])
            .SelectMany(paths => paths.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        Assert.All(advertised, path => Assert.Contains(path, readable));
    }

    [Fact]
    public void DomainsAreStableAcrossLoads()
        => Assert.Equal(Index.Domains, CapabilityIndex.LoadEmbedded().Domains);
}
