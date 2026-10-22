using System.ComponentModel;
using System.Text.Json.Nodes;
using Atlassian.Mcp.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Atlassian.Mcp.Confluence;

/// <summary>
/// Tools that create or change Confluence content. They are registered only when
/// <c>Confluence:EnableWriteTools</c> is true. Each call runs as the signed-in person, so Confluence attributes
/// the change to them and enforces their space and page permissions.
/// </summary>
/// <remarks>
/// Text arguments are plain text with light Markdown and are escaped on the way to storage format, so a model
/// cannot inject markup or macros. Failed writes are never retried automatically.
/// </remarks>
[McpServerToolType]
public sealed class ConfluenceWriteTools(ConfluenceClient confluence, IOptions<LimitOptions> limits)
{
    [McpServerTool(Name = "confluence_create_page", Title = "Create Confluence Page", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Creates a Confluence page as the signed-in user and returns its id and url. Find the numeric space ID with confluence_list_spaces, and a parent page's ID with confluence_search_content or confluence_list_pages. This is not idempotent: if the call fails or you are unsure it worked, look for the page with confluence_list_pages (by title) before trying again, or you will create a duplicate. Confirm the title and space with the user before creating.")]
    public async Task<JsonNode> CreatePage(
        [Description("The numeric ID of the space to create the page in.")] string spaceId,
        [Description("The page title. One line of up to 255 characters. Must be unique within the space.")] string title,
        [Description("The page body. Plain text; blank lines separate paragraphs, '- ' makes bullets, '1. ' makes numbered items, '#' makes headings, and ``` fences code.")] string body,
        [Description("Optional numeric ID of the parent page. Omit to create the page at the top of the space.")] string? parentId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new JsonObject
        {
            ["spaceId"] = Names.Number(spaceId, "space ID"),
            ["status"] = "current",
            ["title"] = Names.Line(title, "title"),
            ["body"] = Storage(StorageFormat.FromText(Names.Body(body, "body", limits.Value.MaxTextChars)))
        };

        if (!string.IsNullOrWhiteSpace(parentId))
        {
            request["parentId"] = Names.Number(parentId, "parent page ID");
        }

        var page = await confluence.SendAsync(HttpMethod.Post, "api/v2/pages", request, cancellationToken);
        return Result(page, "created");
    }

    [McpServerTool(Name = "confluence_update_page", Title = "Update Confluence Page", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Adds to or rewrites a Confluence page as the signed-in user, which creates a new version. The default mode is append, which keeps everything already on the page and adds your text at the end. Mode replace overwrites the whole body, and any macros, tables, or formatting that reading the page as text did not show are lost, so use it only for pages you are writing from scratch. You can also pass only a new title. If someone else edits the page first, the call fails and you should read the page again.")]
    public async Task<JsonNode> UpdatePage(
        [Description("The numeric page ID.")] string pageId,
        [Description("Text to add or write. Plain text with light Markdown. Omit to change only the title.")] string? body = null,
        [Description("Optional new title. One line of up to 255 characters.")] string? title = null,
        [Description("append (default) keeps the existing content and adds to the end; replace overwrites it.")] string mode = "append",
        CancellationToken cancellationToken = default)
    {
        var id = Names.Number(pageId, "page ID");
        var how = (mode ?? "append").Trim().ToLowerInvariant();
        if (how is not ("append" or "replace"))
        {
            throw new McpException("The mode must be append or replace.");
        }

        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(title))
        {
            throw new McpException("Pass a body, a title, or both.");
        }

        var existing = await confluence.GetAsync(
            UpstreamClient.WithQuery($"api/v2/pages/{id}", ("body-format", "storage")), cancellationToken)
            ?? throw new McpException($"Confluence returned no page for {id}.");

        var version = Shape.Int(existing, "version", "number")
            ?? throw new McpException("Confluence did not report the page's current version.");

        var content = Shape.Str(existing, "body", "storage", "value") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            var added = StorageFormat.FromText(Names.Body(body, "body", limits.Value.MaxTextChars));
            content = how == "replace" ? added : content + added;
        }

        var request = new JsonObject
        {
            ["id"] = id,
            ["status"] = Shape.Str(existing, "status") ?? "current",
            ["title"] = string.IsNullOrWhiteSpace(title) ? Shape.Str(existing, "title") : Names.Line(title, "title"),
            ["body"] = Storage(content),
            ["version"] = new JsonObject { ["number"] = version + 1 }
        };

        var page = await confluence.SendAsync(HttpMethod.Put, $"api/v2/pages/{id}", request, cancellationToken);
        return Result(page, "updated");
    }

    [McpServerTool(Name = "confluence_add_comment", Title = "Add Confluence Comment", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Adds a comment at the bottom of a page as the signed-in user. This is not idempotent: a repeated call adds a second comment.")]
    public async Task<JsonNode> AddComment(
        [Description("The numeric page ID.")] string pageId,
        [Description("The comment. Plain text with light Markdown.")] string comment,
        CancellationToken cancellationToken = default)
    {
        var id = Names.Number(pageId, "page ID");
        var request = new JsonObject
        {
            ["pageId"] = id,
            ["body"] = Storage(StorageFormat.FromText(Names.Body(comment, "comment", limits.Value.MaxTextChars)))
        };

        var response = await confluence.SendAsync(HttpMethod.Post, "api/v2/footer-comments", request, cancellationToken);
        return Shape.Obj(
            ("added", true),
            ("commentId", Shape.Str(response, "id")),
            ("pageId", id),
            ("url", Shape.Str(response, "_links", "webui") is { } link ? confluence.WebUrl(link) : null));
    }

    private static JsonObject Storage(string value) => new() { ["representation"] = "storage", ["value"] = value };

    private JsonObject Result(JsonNode? page, string verb) => Shape.Obj(
        (verb, true),
        ("id", Shape.Str(page, "id")),
        ("title", Shape.Str(page, "title")),
        ("spaceId", Shape.Str(page, "spaceId")),
        ("version", Shape.Int(page, "version", "number")),
        ("url", confluence.WebUrl(Shape.Str(page, "_links", "webui"))));
}
