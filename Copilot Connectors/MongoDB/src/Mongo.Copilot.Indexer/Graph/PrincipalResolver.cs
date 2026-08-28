using System.Collections.Concurrent;
using Mongo.Copilot.Core.Access;

namespace Mongo.Copilot.Indexer.Graph;

/// <summary>
/// Resolves the raw values found in MongoDB documents into Entra object IDs.
/// </summary>
/// <remarks>
/// A document rarely stores an Entra object ID. It is far more likely to hold an email
/// address, a user principal name, or a team name — so requiring installers to pre-populate
/// GUIDs would make the ACL mapping unusable against most real data.
/// <para>
/// Values that already parse as a GUID are passed through untouched. Anything else is looked
/// up through Microsoft Graph and cached, because a crawl asks for the same handful of teams
/// tens of thousands of times. Failures are cached too, so an unresolvable value costs one
/// request per crawl rather than one per document.
/// </para>
/// </remarks>
public sealed class PrincipalResolver(GraphClient graph, ILogger<PrincipalResolver> logger)
{
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Values that failed to resolve, reported once per crawl rather than per document.</summary>
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves every entry to an object ID.
    /// </summary>
    /// <returns>
    /// The resolvable subset, or <c>null</c> when nothing resolved. Dropping an unresolvable
    /// entry narrows the audience, which is the safe direction; returning null when the whole
    /// ACL is unresolvable lets the caller skip the document rather than publish it ungoverned.
    /// </returns>
    public async Task<IReadOnlyList<AclEntry>?> ResolveAsync(
        IReadOnlyList<AclEntry> entries,
        CancellationToken cancellationToken)
    {
        var resolved = new List<AclEntry>(entries.Count);

        foreach (var entry in entries)
        {
            // "everyone" is a literal, not a principal to look up.
            if (entry.Type is "everyone")
            {
                resolved.Add(entry);
                continue;
            }

            var objectId = await ResolveOneAsync(entry, cancellationToken);

            if (objectId is null)
            {
                if (_reported.TryAdd(entry.Value, 0))
                {
                    logger.LogWarning(
                        "Could not resolve {Type} '{Value}' to an Entra object ID; documents granted " +
                        "only to it will not be published.", entry.Type, entry.Value);
                }

                continue;
            }

            resolved.Add(entry with { Value = objectId });
        }

        return resolved.Count > 0 ? resolved : null;
    }

    private async Task<string?> ResolveOneAsync(AclEntry entry, CancellationToken cancellationToken)
    {
        // Already an object ID: the common case once a directory is well curated.
        if (Guid.TryParse(entry.Value, out _))
            return entry.Value;

        var key = $"{entry.Type}:{entry.Value}";

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var objectId = entry.Type switch
        {
            "user" => await ResolveUserAsync(entry.Value, cancellationToken),
            "group" => await ResolveGroupAsync(entry.Value, cancellationToken),
            _ => null
        };

        _cache[key] = objectId;
        return objectId;
    }

    private async Task<string?> ResolveUserAsync(string value, CancellationToken cancellationToken)
    {
        // A UPN addresses the user resource directly, which avoids a filtered query.
        if (value.Contains('@', StringComparison.Ordinal))
        {
            var direct = await TryGetAsync<DirectoryObject>(
                $"users/{Uri.EscapeDataString(value)}?$select=id", cancellationToken);

            if (direct?.Id is { Length: > 0 })
                return direct.Id;
        }

        // Fall back to mail and mail nickname, which differ from the UPN often enough to matter.
        var filter = Uri.EscapeDataString(
            $"mail eq '{Escape(value)}' or userPrincipalName eq '{Escape(value)}' or mailNickname eq '{Escape(value)}'");

        var results = await TryGetAsync<DirectoryCollection>(
            $"users?$filter={filter}&$select=id&$top=2", cancellationToken);

        return SingleMatch(results, value, "user");
    }

    private async Task<string?> ResolveGroupAsync(string value, CancellationToken cancellationToken)
    {
        var filter = Uri.EscapeDataString(
            $"displayName eq '{Escape(value)}' or mail eq '{Escape(value)}' or mailNickname eq '{Escape(value)}'");

        var results = await TryGetAsync<DirectoryCollection>(
            $"groups?$filter={filter}&$select=id&$top=2", cancellationToken);

        return SingleMatch(results, value, "group");
    }

    /// <summary>
    /// Accepts a match only when it is unambiguous. Two groups sharing a display name would
    /// otherwise make the choice arbitrary, and an arbitrary ACL is a wrong ACL.
    /// </summary>
    private string? SingleMatch(DirectoryCollection? results, string value, string kind)
    {
        var matches = results?.Value ?? [];

        if (matches.Count == 1)
            return matches[0].Id;

        if (matches.Count > 1 && _reported.TryAdd($"ambiguous:{value}", 0))
        {
            logger.LogWarning(
                "'{Value}' matches more than one {Kind} in the directory; refusing to guess. " +
                "Store the Entra object ID in the document instead.", value, kind);
        }

        return null;
    }

    private async Task<T?> TryGetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await graph.GetAsync<T>(path, cancellationToken);
        }
        catch (GraphException ex)
        {
            logger.LogDebug(ex, "Directory lookup failed for {Path}.", path);
            return default;
        }
    }

    /// <summary>Escapes single quotes for an OData string literal.</summary>
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed class DirectoryObject
    {
        public string? Id { get; set; }
    }

    private sealed class DirectoryCollection
    {
        public List<DirectoryObject> Value { get; set; } = [];
    }
}
