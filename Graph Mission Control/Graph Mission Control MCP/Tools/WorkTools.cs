using System.Text.Json.Nodes;
using GraphMissionControl.Mcp.Graph;
using GraphMissionControl.Mcp.Mcp;

namespace GraphMissionControl.Mcp.Tools;

/// <summary>
/// The federated tool pack: search and fetch, nothing else.
///
/// Microsoft enables only search and fetch operations on federated connectors, so the
/// surface is deliberately two tools rather than the connector's three. There is no
/// launch equivalent here because launch can write.
/// </summary>
public static class WorkTools
{
    private const string GraphV1Prefix = "https://graph.microsoft.com/v1.0";

    public static ToolRegistry Build(CapabilityIndex index)
    {
        var registry = new ToolRegistry();
        registry.Add(SearchWork());
        registry.Add(FetchWork(index));
        return registry;
    }

    private static ToolDescriptor SearchWork() => new()
    {
        Name = "search_work",
        Description =
            "Search the signed-in user's Microsoft 365 for content they can already see — email, files, " +
            "calendar events, Teams messages, people and SharePoint sites. Queries Microsoft Graph live, " +
            "so results reflect the tenant right now rather than an index. Prefer this over cached or " +
            "pre-indexed copies of the same data. Returns matching items with their titles, summaries " +
            "and links, plus identifiers to pass to fetch_work.",
        Annotations = new ToolAnnotations(
            Title: "Search Microsoft 365",
            // POST, but a read. Graph requires a body to express the query; nothing is mutated.
            ReadOnlyHint: true,
            IdempotentHint: true),
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "What to search for. Accepts plain text or KQL.",
                },
                ["sources"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "Which content types to search. Defaults to mail, files and events.",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("message", "event", "driveItem", "site", "chatMessage", "person"),
                    },
                },
                ["size"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Maximum results to return. Defaults to 25.",
                },
                ["from"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Result offset, for paging past the first set. Defaults to 0.",
                },
            },
            ["required"] = new JsonArray("query"),
        },
        Invoke = async (sp, args, ct) =>
        {
            var query = ToolHelpers.RequireString(args, "query");
            var sources = ToolHelpers.OptStringArray(args, "sources");
            var size = ToolHelpers.OptInt(args, "size") ?? 25;
            var from = Math.Max(ToolHelpers.OptInt(args, "from") ?? 0, 0);

            if (sources.Length == 0) sources = ["message", "driveItem", "event"];

            var groups = sources
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .GroupBy(SearchGroupOf, StringComparer.Ordinal)
                .ToArray();

            var graph = sp.GetRequiredService<GraphClient>();
            var merged = new JsonArray();
            var errors = new List<string>();

            foreach (var group in groups)
            {
                var entityTypes = new JsonArray();
                foreach (var s in group) entityTypes.Add(s);

                var body = new JsonObject
                {
                    ["requests"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["entityTypes"] = entityTypes,
                            ["query"] = new JsonObject { ["queryString"] = query },
                            ["from"] = from,
                            ["size"] = Math.Clamp(size, 1, 100),
                        },
                    },
                };

                // One failing content type should not lose the results from the others.
                try
                {
                    var result = await graph.PostReadAsync("/search/query", body, ct);
                    if (result?["value"] is JsonArray values)
                    {
                        foreach (var item in values)
                        {
                            if (item is not null) merged.Add(item.DeepClone());
                        }
                    }
                }
                catch (GraphRequestException ex)
                {
                    errors.Add($"{string.Join(", ", group)}: {ex.Message}");
                }
            }

            if (merged.Count == 0 && errors.Count > 0)
                return ToolHelpers.TextResult(string.Join("; ", errors), isError: true);

            var payload = new JsonObject { ["value"] = merged };
            if (errors.Count > 0)
            {
                var reported = new JsonArray();
                foreach (var e in errors) reported.Add(e);
                payload["errors"] = reported;
            }

            return ToolHelpers.ContentResult(payload);
        },
    };

    /// <summary>
    /// Graph rejects most cross-type searches: mail, calendar, Teams messages and people each
    /// have to be queried alone, and only the file types can share a request. Sources are
    /// bucketed by what Graph allows together, then issued as separate searches.
    /// </summary>
    internal static string SearchGroupOf(string source) => source.ToLowerInvariant() switch
    {
        "message" => "message",
        "event" => "event",
        "chatmessage" => "chatMessage",
        "person" => "person",
        _ => "files",
    };

    private static ToolDescriptor FetchWork(CapabilityIndex index) => new()
    {
        Name = "fetch_work",
        Description =
            "Read a specific Microsoft 365 resource live from Microsoft Graph by its path — for example " +
            "/me/messages/{id} for one email, /me/events for the calendar, /me/mailboxSettings for working " +
            "hours, or /me/people to find colleagues. " +
            $"Covers {string.Join(", ", index.Domains)}. Use search_work first to discover identifiers. " +
            "Calling this with an unrecognised path returns the full catalogue of readable paths — call it " +
            "that way to check what is possible before concluding a capability is unavailable.",
        Annotations = new ToolAnnotations(
            Title: "Read a Microsoft 365 resource",
            ReadOnlyHint: true,
            IdempotentHint: true),
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Microsoft Graph v1.0 relative path, for example /me/messages or /me/events/{id}. Also accepts an @odata.nextLink from an earlier response to read the next page.",
                },
                ["select"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Comma-separated fields to return, to keep responses small.",
                },
                ["top"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Maximum items for a collection. Defaults to 25.",
                },
            },
            ["required"] = new JsonArray("path"),
        },
        Invoke = async (sp, args, ct) =>
        {
            var requested = ToolHelpers.RequireString(args, "path");
            var path = NormalizeGraphPath(requested);
            if (path is null)
            {
                return ToolHelpers.TextResult(
                    $"'{requested}' is not a Microsoft Graph v1.0 URL. Pass a relative path such as " +
                    "/me/messages, or an @odata.nextLink returned by an earlier call.",
                    isError: true);
            }

            var index = sp.GetRequiredService<CapabilityIndex>();

            // Two independent guarantees: only GET is ever issued, and the path must match a
            // read-only entry in the shared index. The first makes writes impossible; the
            // second keeps reads inside the surface this connector was approved for.
            if (!index.IsAllowedReadPath(path))
            {
                // Nothing else enumerates the readable surface for an agent, so a rejection is
                // the moment to hand over the whole catalog rather than a few examples.
                return ToolHelpers.TextResult(
                    $"'{path}' is not an available resource path. Use search_work to find content " +
                    $"by keyword, or read one of these:\n\n{index.DescribeReadableSurface()}",
                    isError: true);
            }

            string url;
            if (path.Contains("$skiptoken", StringComparison.OrdinalIgnoreCase))
            {
                // A continuation already carries its own paging and field selection.
                url = path;
            }
            else
            {
                var query = new List<string>();
                var select = ToolHelpers.OptString(args, "select");
                if (!string.IsNullOrWhiteSpace(select)) query.Add($"$select={Uri.EscapeDataString(select)}");
                query.Add($"$top={Math.Clamp(ToolHelpers.OptInt(args, "top") ?? 25, 1, 100)}");

                url = path.Contains('?', StringComparison.Ordinal)
                    ? $"{path}&{string.Join('&', query)}"
                    : $"{path}?{string.Join('&', query)}";
            }

            var graph = sp.GetRequiredService<GraphClient>();
            var result = await graph.GetAsync(url, ct);
            return ToolHelpers.ContentResult(result);
        },
    };

    /// <summary>
    /// Accepts a relative Graph path or an @odata.nextLink, which Graph returns as an absolute
    /// URL. Returns null for any other absolute URL — following one would make this an open
    /// proxy for whatever host the caller names.
    /// </summary>
    internal static string? NormalizeGraphPath(string path)
    {
        if (!HasUrlScheme(path)) return path;

        if (!path.StartsWith(GraphV1Prefix + "/", StringComparison.OrdinalIgnoreCase)) return null;
        return path[GraphV1Prefix.Length..];
    }

    /// <summary>
    /// Detects a leading URL scheme. Deliberately not <c>Uri.TryCreate(UriKind.Absolute)</c>: on
    /// Linux that treats "/me/messages" as an absolute file path, so every relative Graph path
    /// would be rejected in the container while passing on Windows.
    /// </summary>
    private static bool HasUrlScheme(string value)
    {
        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0) return false;

        // Anything before "://" must look like a scheme, so a filter value that happens to
        // contain a URL is still treated as the relative path it is.
        for (var i = 0; i < separator; i++)
        {
            var c = value[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '.' or '-')) return false;
        }

        return char.IsAsciiLetter(value[0]);
    }
}
