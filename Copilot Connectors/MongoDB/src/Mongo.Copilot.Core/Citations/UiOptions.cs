using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core.Citations;

/// <summary>
/// Per-collection citation overrides.
/// </summary>
public sealed class UiCollectionOptions
{
    /// <summary>Absolute template, overriding both the base URL and the profile's UrlPath.</summary>
    public string? Url { get; set; }

    /// <summary>Host for this collection, combined with the profile's UrlPath.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Where documents are viewable. Supplied by the installing developer, because the
/// location is a property of the environment rather than of the data.
/// </summary>
public sealed class UiOptions
{
    public string? BaseUrl { get; set; }

    /// <summary>
    /// When true (the default) the indexer refuses to start if any indexed collection
    /// resolves to no URL, rather than publishing a large batch of uncited items.
    /// </summary>
    public bool RequireUrl { get; set; } = true;

    public Dictionary<string, UiCollectionOptions> Collections { get; set; } = [];

    /// <summary>
    /// Whether a citation template exists for this profile, without needing a document.
    /// Used by startup validation.
    /// </summary>
    public bool CanResolve(CollectionProfile profile) => TemplateFor(profile) is not null;

    /// <summary>
    /// Resolution order, first match wins:
    /// per-collection absolute Url, per-collection BaseUrl + UrlPath, global BaseUrl + UrlPath.
    /// </summary>
    internal string? TemplateFor(CollectionProfile profile)
    {
        Collections.TryGetValue(profile.Name, out var perCollection);

        if (!string.IsNullOrWhiteSpace(perCollection?.Url))
            return perCollection!.Url;

        if (string.IsNullOrWhiteSpace(profile.UrlPath))
            return null;

        var baseUrl = !string.IsNullOrWhiteSpace(perCollection?.BaseUrl)
            ? perCollection!.BaseUrl
            : BaseUrl;

        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        return string.Concat(baseUrl!.TrimEnd('/'), "/", profile.UrlPath!.TrimStart('/'));
    }

    /// <summary>
    /// Stable fingerprint of everything that affects resolved URLs. A change means
    /// already-published items carry stale links and the connection needs a re-crawl.
    /// </summary>
    public string Fingerprint(IEnumerable<CollectionProfile> profiles)
    {
        var parts = profiles
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Name}={TemplateFor(p) ?? "<none>"}");

        var payload = string.Join("|", parts);
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexString(bytes);
    }
}
