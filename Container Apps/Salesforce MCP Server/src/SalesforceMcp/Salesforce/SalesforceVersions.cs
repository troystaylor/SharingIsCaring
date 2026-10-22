using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace SalesforceMcp.Salesforce;

/// <summary>
/// Chooses the REST API version for a call: the caller's, if given and supported, otherwise the server default.
/// </summary>
public sealed partial class SalesforceVersions(SalesforceClient salesforce, MetadataCache cache, TimeProvider time)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    /// <summary>
    /// Accepts <c>v52.0</c>, <c>52.0</c>, <c>v52</c> and <c>52</c>, and returns <c>v52.0</c>.
    /// Anything else is rejected, so a value can never put extra path or query text into a Salesforce URL.
    /// </summary>
    public static string Normalize(string requested)
    {
        var match = VersionPattern().Match(requested);
        if (!match.Success)
        {
            throw new McpException(
                $"'{Shorten(requested)}' is not an API version. Use a form like v52.0. Call list_api_versions to see what the org supports.");
        }

        return $"v{match.Groups[1].Value}.0";
    }

    public async Task<IReadOnlyList<ApiVersionInfo>> ListAsync(CancellationToken cancellationToken)
    {
        if (cache.Versions is { } cached && time.GetUtcNow() - cached.FetchedAt < CacheFor)
        {
            return cached.Items;
        }

        // The version list is the one resource that is not under /services/data/{version}.
        var response = await salesforce.GetAsync("/services/data/", cancellationToken);

        var items = (response as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(o => (Version: (string?)o["version"], Label: (string?)o["label"]))
            .Where(v => v.Version is not null && VersionPattern().IsMatch(v.Version))
            .Select(v => new ApiVersionInfo(Normalize(v.Version!), v.Label ?? string.Empty))
            .OrderBy(v => Number(v.Version))
            .ToList();

        if (items.Count == 0)
        {
            throw new McpException("Salesforce did not return a list of API versions.");
        }

        cache.Versions = new MetadataCache.VersionList(items, time.GetUtcNow());
        return items;
    }

    /// <summary>
    /// Returns the version to use. A caller-supplied version that differs from the server default is
    /// checked against what the org supports, so a typo or a retired version fails with a clear message
    /// instead of a 404 from Salesforce. If the list cannot be fetched, the version is passed through and
    /// Salesforce decides.
    /// </summary>
    public async Task<string> ResolveAsync(string? requested, CancellationToken cancellationToken)
    {
        var fallback = salesforce.Settings.ApiVersion;
        if (string.IsNullOrWhiteSpace(requested))
        {
            return fallback;
        }

        var version = Normalize(requested);
        if (version == fallback)
        {
            return version;
        }

        IReadOnlyList<ApiVersionInfo> supported;
        try
        {
            supported = await ListAsync(cancellationToken);
        }
        catch (McpException) when (!cancellationToken.IsCancellationRequested)
        {
            return version;
        }

        if (supported.All(v => v.Version != version))
        {
            throw new McpException(
                $"This Salesforce org does not support API version {version}. It supports {supported[0].Version} through {supported[^1].Version}. Call list_api_versions for the full list.");
        }

        return version;
    }

    public static double Number(string version) =>
        double.Parse(version.TrimStart('v'), CultureInfo.InvariantCulture);

    private static string Shorten(string value) => value.Length <= 20 ? value : value[..20] + "...";

    [GeneratedRegex(@"^\s*v?(\d{2,3})(?:\.0)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
}
