using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Atlassian.Mcp.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Atlassian.Mcp.Confluence;

/// <summary>
/// Read-only tools over the Confluence Cloud REST API. Each call runs as the person who is signed in to
/// Atlassian, so Confluence's own space and page permissions decide what comes back for them.
/// </summary>
/// <remarks>
/// An empty result is a success and is returned as one. Page bodies come back as readable text, capped.
/// </remarks>
[McpServerToolType]
public sealed partial class ConfluenceReadTools(ConfluenceClient confluence, IOptions<LimitOptions> limits)
{
    [GeneratedRegex(@"@@@(end)?hl@@@")]
    private static partial Regex HighlightMarks();

    [McpServerTool(Name = "confluence_get_current_user", Title = "Get Confluence Connection Info", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Shows who the Confluence requests run as, which Atlassian site they go to, and the scopes the sign-in holds on it. Use it first when a call is refused, or to confirm the connection.")]
    public async Task<JsonNode> GetCurrentUser(CancellationToken cancellationToken = default)
    {
        var me = await confluence.GetAsync("rest/api/user/current", cancellationToken);
        return Shape.Obj(
            ("site", confluence.Settings.Site),
            ("accountId", Shape.Str(me, "accountId")),
            ("displayName", Shape.Str(me, "displayName") ?? Shape.Str(me, "publicName")),
            ("email", Shape.Str(me, "email")),
            ("scopes", confluence.Who.Scopes));
    }

    [McpServerTool(Name = "confluence_search_content", Title = "Search Confluence", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Searches Confluence pages, blog posts, and attachments with CQL and returns matches with an excerpt and a url that opens each one. Use it to find documentation, for example: type = page AND text ~ \"release process\" AND space = ENG. To read a result, pass its id to confluence_get_page. If nextStart is present, pass it as start for the next page.")]
    public async Task<JsonNode> SearchContent(
        [Description("A CQL query, such as: type = page AND title ~ \"onboarding\". Use text ~ \"words\" to search page bodies.")] string cql,
        [Description("Most results to return, from 1 to 100. Defaults to 25.")] int limit = 25,
        [Description("Zero-based offset of the first result, from the previous call's nextStart. Defaults to 0.")] int start = 0,
        CancellationToken cancellationToken = default)
    {
        var query = Names.Query(cql, "CQL");
        var count = Shape.Clamp(limit, 1, Math.Min(100, limits.Value.MaxItems));
        var offset = Math.Max(0, start);

        var response = await confluence.GetAsync(
            UpstreamClient.WithQuery(
                "rest/api/search",
                ("cql", query),
                ("limit", count.ToString()),
                ("start", offset.ToString())),
            cancellationToken);

        var (results, truncated) = Shape.Limit(
            Shape.Items(response, "results").Select(r => (JsonNode?)Shape.Obj(
                ("id", Shape.Str(r, "content", "id")),
                ("type", Shape.Str(r, "content", "type") ?? Shape.Str(r, "entityType")),
                ("title", Shape.Str(r, "title")),
                ("space", Shape.Str(r, "resultGlobalContainer", "title")),
                ("excerpt", Excerpt(Shape.Str(r, "excerpt"))),
                ("lastModified", Shape.Str(r, "lastModified")),
                ("url", confluence.WebUrl(Shape.Str(r, "url") ?? Shape.Str(r, "content", "_links", "webui"))))),
            count,
            limits.Value.MaxResponseChars);

        var result = Shape.Obj(("returned", results.Count), ("results", results));
        if (Shape.At(response, "_links", "next") is not null)
        {
            result["nextStart"] = offset + count;
        }

        if (truncated)
        {
            result["note"] = "Some results were left out to keep the response small. Narrow the CQL.";
        }

        return result;
    }

    [McpServerTool(Name = "confluence_get_page", Title = "Get Confluence Page", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Reads one Confluence page by ID and returns its title, version, and body as plain text, with a url that opens it. Long pages are cut at a size limit and say so. Use it after confluence_search_content or confluence_list_pages.")]
    public async Task<JsonNode> GetPage(
        [Description("The numeric page ID, such as 123456.")] string pageId,
        CancellationToken cancellationToken = default)
    {
        var id = Names.Number(pageId, "page ID");
        var page = await confluence.GetAsync(
            UpstreamClient.WithQuery($"api/v2/pages/{id}", ("body-format", "storage")), cancellationToken);

        if (page is null)
        {
            throw new McpException($"Confluence returned no page for {id}.");
        }

        var text = StorageFormat.ToText(Shape.Str(page, "body", "storage", "value"), int.MaxValue);
        var body = Shape.Cap(text, limits.Value.MaxTextChars, out var truncated);

        var result = PageSummary(page);
        result["body"] = body;
        result["authorId"] = Shape.Str(page, "authorId");
        result["createdAt"] = Shape.Str(page, "createdAt");
        result["versionMessage"] = Shape.Str(page, "version", "message");
        if (truncated)
        {
            result["truncated"] = true;
            result["note"] = "The page is longer than the size limit, so the end was left out. Open the url for the full page.";
        }

        return result;
    }

    [McpServerTool(Name = "confluence_list_pages", Title = "List Confluence Pages", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists pages, optionally in one space or with an exact title. Use it to browse a space or to look up a page by title. For keyword search use confluence_search_content. If nextCursor is present, pass it as cursor for the next page.")]
    public async Task<JsonNode> ListPages(
        [Description("Optional numeric space ID from confluence_list_spaces.")] string? spaceId = null,
        [Description("Optional exact page title.")] string? title = null,
        [Description("Most pages to return, from 1 to 100. Defaults to 25.")] int limit = 25,
        [Description("The nextCursor from the previous result, unchanged.")] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var count = Shape.Clamp(limit, 1, Math.Min(100, limits.Value.MaxItems));
        var response = await confluence.GetAsync(
            UpstreamClient.WithQuery(
                "api/v2/pages",
                ("space-id", string.IsNullOrWhiteSpace(spaceId) ? null : Names.Number(spaceId, "space ID")),
                ("title", Title(title)),
                ("status", "current"),
                ("limit", count.ToString()),
                ("cursor", cursor is null ? null : Names.Cursor(cursor))),
            cancellationToken);

        return PagedResult(response, "pages", count, p => PageSummary(p));
    }

    [McpServerTool(Name = "confluence_get_page_children", Title = "Get Confluence Child Pages", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the direct child pages of a page, to walk a page tree. If nextCursor is present, pass it as cursor for the next page.")]
    public async Task<JsonNode> GetPageChildren(
        [Description("The numeric ID of the parent page.")] string pageId,
        [Description("Most children to return, from 1 to 100. Defaults to 25.")] int limit = 25,
        [Description("The nextCursor from the previous result, unchanged.")] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var id = Names.Number(pageId, "page ID");
        var count = Shape.Clamp(limit, 1, Math.Min(100, limits.Value.MaxItems));
        var response = await confluence.GetAsync(
            UpstreamClient.WithQuery(
                $"api/v2/pages/{id}/children",
                ("limit", count.ToString()),
                ("cursor", cursor is null ? null : Names.Cursor(cursor))),
            cancellationToken);

        return PagedResult(response, "children", count, p => Shape.Obj(
            ("id", Shape.Str(p, "id")),
            ("title", Shape.Str(p, "title")),
            ("status", Shape.Str(p, "status")),
            ("spaceId", Shape.Str(p, "spaceId"))));
    }

    [McpServerTool(Name = "confluence_list_spaces", Title = "List Confluence Spaces", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the spaces the signed-in user can see, optionally limited to some space keys. Use it to find a space's numeric ID for confluence_list_pages. If nextCursor is present, pass it as cursor for the next page.")]
    public async Task<JsonNode> ListSpaces(
        [Description("Optional space keys to look up, such as [\"ENG\",\"HR\"].")] string[]? keys = null,
        [Description("Most spaces to return, from 1 to 100. Defaults to 25.")] int limit = 25,
        [Description("The nextCursor from the previous result, unchanged.")] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var count = Shape.Clamp(limit, 1, Math.Min(100, limits.Value.MaxItems));
        var spaceKeys = keys is { Length: > 0 }
            ? string.Join(',', keys.Take(Names.MaxFieldCount).Select(Names.SpaceKey))
            : null;

        var response = await confluence.GetAsync(
            UpstreamClient.WithQuery(
                "api/v2/spaces",
                ("keys", spaceKeys),
                ("limit", count.ToString()),
                ("cursor", cursor is null ? null : Names.Cursor(cursor))),
            cancellationToken);

        return PagedResult(response, "spaces", count, s => Shape.Obj(
            ("id", Shape.Str(s, "id")),
            ("key", Shape.Str(s, "key")),
            ("name", Shape.Str(s, "name")),
            ("type", Shape.Str(s, "type")),
            ("status", Shape.Str(s, "status")),
            ("url", Shape.Str(s, "key") is { } key ? confluence.WebUrl($"/spaces/{key}") : null)));
    }

    private JsonObject PageSummary(JsonNode page) => Shape.Obj(
        ("id", Shape.Str(page, "id")),
        ("title", Shape.Str(page, "title")),
        ("status", Shape.Str(page, "status")),
        ("spaceId", Shape.Str(page, "spaceId")),
        ("parentId", Shape.Str(page, "parentId")),
        ("version", Shape.Int(page, "version", "number")),
        ("lastModified", Shape.Str(page, "version", "createdAt")),
        ("url", confluence.WebUrl(Shape.Str(page, "_links", "webui"))));

    private JsonNode PagedResult(JsonNode? response, string name, int count, Func<JsonNode, JsonObject> map)
    {
        var (items, truncated) = Shape.Limit(
            Shape.Items(response, "results").Select(i => (JsonNode?)map(i)), count, limits.Value.MaxResponseChars);

        var result = Shape.Obj(("returned", items.Count), (name, items));
        if (Shape.CursorFrom(Shape.Str(response, "_links", "next")) is { } next)
        {
            result["nextCursor"] = next;
        }

        if (truncated)
        {
            result["note"] = "Some results were left out to keep the response small. Request fewer items.";
        }

        return result;
    }

    private static string? Title(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var text = title.Trim();
        return text.Length > 255 || text.Any(char.IsControl)
            ? throw new McpException("The title must be up to 255 characters.")
            : text;
    }

    private static string? Excerpt(string? excerpt) =>
        excerpt is null ? null : HighlightMarks().Replace(excerpt, string.Empty).Trim();
}
