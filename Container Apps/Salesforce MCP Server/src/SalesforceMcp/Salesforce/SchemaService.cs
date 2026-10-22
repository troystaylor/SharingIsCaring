using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using SalesforceMcp.Configuration;

namespace SalesforceMcp.Salesforce;

/// <summary>Where a schema response came from, so a caller can judge how fresh it is.</summary>
public enum SchemaSource
{
    /// <summary>Served from memory, inside the cache window.</summary>
    Cache,

    /// <summary>The cache window had passed; Salesforce confirmed nothing changed (304) and no payload was sent.</summary>
    Revalidated,

    /// <summary>Fetched from Salesforce.</summary>
    Fetched
}

public sealed record SchemaResult(JsonObject Body, DateTimeOffset RetrievedAt, SchemaSource Source)
{
    public JsonObject Meta() => new()
    {
        ["retrievedAt"] = RetrievedAt.UtcDateTime.ToString("O"),
        ["source"] = Source switch
        {
            SchemaSource.Cache => "cache",
            SchemaSource.Revalidated => "revalidated",
            _ => "fetched"
        }
    };
}

/// <summary>
/// Object descriptions, cached. Salesforce object schemas change rarely and describe calls are large, so a
/// description is kept for <see cref="SalesforceOptions.SchemaCacheSeconds"/>, then revalidated with
/// If-Modified-Since rather than downloaded again.
/// </summary>
/// <remarks>
/// A description is specific to the API version and to the person asking, because field-level security means
/// two users can see different fields of the same object. The cache key therefore includes both. Serving one
/// user's description to another would reveal fields they are not allowed to see, so the user is part of the
/// key and descriptions are never shared between people. If Salesforce sends no Last-Modified header, a
/// description is simply fetched again after the window.
/// </remarks>
public sealed class SchemaService(
    SalesforceClient salesforce,
    MetadataCache cache,
    TimeProvider time,
    IOptions<SalesforceOptions> options)
{
    public Task<SchemaResult> DescribeAsync(string sobject, string version, CancellationToken cancellationToken) =>
        GetAsync(
            $"describe|{version}|{sobject.ToLowerInvariant()}",
            salesforce.DataPath($"sobjects/{sobject}/describe", version),
            cancellationToken);

    /// <summary>The list of every object the signed-in user can see.</summary>
    public Task<SchemaResult> GlobalDescribeAsync(string version, CancellationToken cancellationToken) =>
        GetAsync($"global|{version}", salesforce.DataPath("sobjects", version), cancellationToken);

    private async Task<SchemaResult> GetAsync(string resourceKey, string path, CancellationToken cancellationToken)
    {
        var key = $"{salesforce.User.CacheKey}|{resourceKey}";
        var settings = options.Value;
        var window = TimeSpan.FromSeconds(settings.SchemaCacheSeconds);

        if (cache.TryGet(key, out var entry))
        {
            var age = time.GetUtcNow() - entry.FetchedAt;
            if (age < window)
            {
                return new SchemaResult(Parse(entry.Json), entry.FetchedAt, SchemaSource.Cache);
            }

            if (entry.LastModified is not null)
            {
                var check = await salesforce.GetConditionalAsync(path, entry.LastModified, cancellationToken);
                if (check.NotModified)
                {
                    var now = time.GetUtcNow();
                    cache.Put(key, entry with { FetchedAt = now }, settings.SchemaCacheMaxEntries);
                    return new SchemaResult(Parse(entry.Json), now, SchemaSource.Revalidated);
                }

                return Store(key, check, settings);
            }
        }

        return Store(key, await salesforce.GetConditionalAsync(path, null, cancellationToken), settings);
    }

    private SchemaResult Store(string key, ConditionalResponse response, SalesforceOptions settings)
    {
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new McpException("Salesforce returned an empty description.");
        }

        var json = Encoding.UTF8.GetBytes(response.Text);
        var body = Parse(json);
        var now = time.GetUtcNow();

        cache.Put(key, new MetadataCache.SchemaEntry(json, response.LastModified, now), settings.SchemaCacheMaxEntries);
        return new SchemaResult(body, now, SchemaSource.Fetched);
    }

    private static JsonObject Parse(byte[] json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject
                ?? throw new McpException("Salesforce returned a description in an unexpected shape.");
        }
        catch (System.Text.Json.JsonException)
        {
            throw new McpException("Salesforce returned a response that was not valid JSON.");
        }
    }
}
