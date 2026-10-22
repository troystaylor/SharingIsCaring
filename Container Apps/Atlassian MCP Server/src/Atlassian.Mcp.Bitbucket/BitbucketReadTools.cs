using System.ComponentModel;
using System.Text.Json.Nodes;
using Atlassian.Mcp.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Atlassian.Mcp.Bitbucket;

/// <summary>
/// Read-only tools over the Bitbucket Cloud REST API. Each call runs as the person who is signed in to
/// Bitbucket, so Bitbucket's own workspace and repository permissions decide what comes back for them.
/// </summary>
/// <remarks>
/// Lists are paged by page number, not by a URL, so a model never supplies an address. An empty result is a
/// success and is returned as one. Diffs and files are capped and say so.
/// </remarks>
[McpServerToolType]
public sealed class BitbucketReadTools(BitbucketClient bitbucket, IOptions<LimitOptions> limits)
{
    private static readonly string[] PullRequestStates = ["OPEN", "MERGED", "DECLINED", "SUPERSEDED"];

    [McpServerTool(Name = "bitbucket_get_current_user", Title = "Get Bitbucket Connection Info", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Shows who the Bitbucket requests run as. Use it first when a call is refused, or to confirm the connection.")]
    public async Task<JsonNode> GetCurrentUser(CancellationToken cancellationToken = default)
    {
        var me = await bitbucket.GetAsync("user", cancellationToken);
        return Shape.Obj(
            ("displayName", Shape.Str(me, "display_name")),
            ("nickname", Shape.Str(me, "nickname")),
            ("accountId", Shape.Str(me, "account_id")),
            ("uuid", Shape.Str(me, "uuid")),
            ("url", Shape.Str(me, "links", "html", "href")),
            ("pinnedWorkspace", bitbucket.Settings.Workspace));
    }

    [McpServerTool(Name = "bitbucket_list_workspaces", Title = "List Bitbucket Workspaces", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the Bitbucket workspaces the signed-in user belongs to. Use it to find a workspace slug for the other Bitbucket tools. If hasMore is true, pass nextPage as page for the next batch.")]
    public async Task<JsonNode> ListWorkspaces(
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Most items per page, from 1 to 100. Defaults to 25.")] int pagelen = 25,
        CancellationToken cancellationToken = default)
    {
        var (count, number) = Paging(page, pagelen);
        var response = await bitbucket.GetAsync(
            UpstreamClient.WithQuery("workspaces", ("pagelen", count.ToString()), ("page", number.ToString())),
            cancellationToken);

        return Paged(response, "workspaces", count, number, w => Shape.Obj(
            ("slug", Shape.Str(w, "slug")),
            ("name", Shape.Str(w, "name")),
            ("uuid", Shape.Str(w, "uuid")),
            ("isPrivate", Shape.Bool(w, "is_private")),
            ("url", Shape.Str(w, "links", "html", "href"))));
    }

    [McpServerTool(Name = "bitbucket_list_repositories", Title = "List Bitbucket Repositories", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the repositories in a workspace that the signed-in user can see, optionally filtered by name, most recently updated first. If hasMore is true, pass nextPage as page for the next batch.")]
    public async Task<JsonNode> ListRepositories(
        [Description("The workspace slug, such as my-team.")] string workspace,
        [Description("Optional text the repository name must contain.")] string? nameContains = null,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Most items per page, from 1 to 100. Defaults to 25.")] int pagelen = 25,
        CancellationToken cancellationToken = default)
    {
        var ws = bitbucket.Workspace(workspace);
        var (count, number) = Paging(page, pagelen);
        var filter = string.IsNullOrWhiteSpace(nameContains)
            ? null
            : $"name ~ \"{Names.SafeText(nameContains, "name filter")}\"";

        var response = await bitbucket.GetAsync(
            UpstreamClient.WithQuery(
                $"repositories/{ws}",
                ("q", filter),
                ("sort", "-updated_on"),
                ("pagelen", count.ToString()),
                ("page", number.ToString())),
            cancellationToken);

        return Paged(response, "repositories", count, number, Repository);
    }

    [McpServerTool(Name = "bitbucket_get_repository", Title = "Get Bitbucket Repository", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Reads one repository's details: description, main branch, language, size, and a url that opens it.")]
    public async Task<JsonNode> GetRepository(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var response = await bitbucket.GetAsync($"repositories/{ws}/{repo}", cancellationToken)
            ?? throw new McpException($"Bitbucket returned no repository for {ws}/{repo}.");

        var result = Repository(response);
        result["size"] = Shape.Int(response, "size");
        result["hasIssues"] = Shape.Bool(response, "has_issues");
        result["forkPolicy"] = Shape.Str(response, "fork_policy");
        result["created"] = Shape.Str(response, "created_on");
        return result;
    }

    [McpServerTool(Name = "bitbucket_list_pull_requests", Title = "List Bitbucket Pull Requests", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists pull requests in a repository, newest first, filtered by state (OPEN, MERGED, DECLINED, or SUPERSEDED). To read one in full, call bitbucket_get_pull_request. If hasMore is true, pass nextPage as page for the next batch.")]
    public async Task<JsonNode> ListPullRequests(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("OPEN, MERGED, DECLINED, or SUPERSEDED. Defaults to OPEN.")] string state = "OPEN",
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Most items per page, from 1 to 50. Defaults to 25.")] int pagelen = 25,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var wanted = (state ?? "OPEN").Trim().ToUpperInvariant();
        if (!PullRequestStates.Contains(wanted))
        {
            throw new McpException("The state must be OPEN, MERGED, DECLINED, or SUPERSEDED.");
        }

        var (count, number) = Paging(page, pagelen);
        var response = await bitbucket.GetAsync(
            UpstreamClient.WithQuery(
                $"repositories/{ws}/{repo}/pullrequests",
                ("state", wanted),
                ("pagelen", count.ToString()),
                ("page", number.ToString())),
            cancellationToken);

        return Paged(response, "pullRequests", count, number, p => PullRequest(p, full: false));
    }

    [McpServerTool(Name = "bitbucket_get_pull_request", Title = "Get Bitbucket Pull Request", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Reads one pull request: title, description, state, author, branches, reviewers and their approvals, comment count, and a url that opens it. For the changes use bitbucket_get_pull_request_diffstat or bitbucket_get_pull_request_diff.")]
    public async Task<JsonNode> GetPullRequest(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("The pull request number.")] string pullRequestId,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var id = Names.Number(pullRequestId, "pull request number");
        var response = await bitbucket.GetAsync($"repositories/{ws}/{repo}/pullrequests/{id}", cancellationToken)
            ?? throw new McpException($"Bitbucket returned no pull request {id}.");

        return PullRequest(response, full: true);
    }

    [McpServerTool(Name = "bitbucket_get_pull_request_diffstat", Title = "Get Pull Request Diffstat", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the files a pull request changes, with how many lines were added and removed in each. Use it for a quick sense of size and scope before reading the diff. If hasMore is true, pass nextPage as page for the next batch.")]
    public async Task<JsonNode> GetPullRequestDiffstat(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("The pull request number.")] string pullRequestId,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Most items per page, from 1 to 100. Defaults to 50.")] int pagelen = 50,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var id = Names.Number(pullRequestId, "pull request number");
        var (count, number) = Paging(page, pagelen);

        var response = await bitbucket.GetAsync(
            UpstreamClient.WithQuery(
                $"repositories/{ws}/{repo}/pullrequests/{id}/diffstat",
                ("pagelen", count.ToString()),
                ("page", number.ToString())),
            cancellationToken);

        return Paged(response, "files", count, number, f => Shape.Obj(
            ("status", Shape.Str(f, "status")),
            ("path", Shape.Str(f, "new", "path") ?? Shape.Str(f, "old", "path")),
            ("oldPath", Shape.Str(f, "old", "path") is { } old && old != Shape.Str(f, "new", "path") ? old : null),
            ("linesAdded", Shape.Int(f, "lines_added")),
            ("linesRemoved", Shape.Int(f, "lines_removed"))));
    }

    [McpServerTool(Name = "bitbucket_get_pull_request_diff", Title = "Get Pull Request Diff", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Returns the unified diff of a pull request as text. Large diffs are cut at a size limit and say so, so call bitbucket_get_pull_request_diffstat first to see how big the change is. Use it to review what changed.")]
    public async Task<JsonNode> GetPullRequestDiff(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("The pull request number.")] string pullRequestId,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var id = Names.Number(pullRequestId, "pull request number");

        var diff = await bitbucket.TextAsync($"repositories/{ws}/{repo}/pullrequests/{id}/diff", cancellationToken);
        var text = Shape.Cap(diff, limits.Value.MaxTextChars, out var truncated);

        var result = Shape.Obj(("pullRequest", id), ("diff", text), ("characters", diff.Length));
        if (truncated)
        {
            result["truncated"] = true;
            result["note"] = "The diff is longer than the size limit, so the end was left out. Use the diffstat to see every file.";
        }

        return result;
    }

    [McpServerTool(Name = "bitbucket_list_pull_request_comments", Title = "List Pull Request Comments", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the comments on a pull request, including inline comments with the file and line they are on. If hasMore is true, pass nextPage as page for the next batch.")]
    public async Task<JsonNode> ListPullRequestComments(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("The pull request number.")] string pullRequestId,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Most items per page, from 1 to 100. Defaults to 25.")] int pagelen = 25,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var id = Names.Number(pullRequestId, "pull request number");
        var (count, number) = Paging(page, pagelen);

        var response = await bitbucket.GetAsync(
            UpstreamClient.WithQuery(
                $"repositories/{ws}/{repo}/pullrequests/{id}/comments",
                ("pagelen", count.ToString()),
                ("page", number.ToString())),
            cancellationToken);

        return Paged(response, "comments", count, number, c => Shape.Obj(
            ("id", Shape.Int(c, "id")),
            ("author", Shape.Str(c, "user", "display_name")),
            ("created", Shape.Str(c, "created_on")),
            ("text", Shape.Cap(Shape.Str(c, "content", "raw"), 4000, out _)),
            ("deleted", Shape.Bool(c, "deleted") is true ? true : null),
            ("path", Shape.Str(c, "inline", "path")),
            ("line", Shape.Int(c, "inline", "to") ?? Shape.Int(c, "inline", "from")),
            ("replyTo", Shape.Int(c, "parent", "id")),
            ("url", Shape.Str(c, "links", "html", "href"))));
    }

    [McpServerTool(Name = "bitbucket_list_commits", Title = "List Bitbucket Commits", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists recent commits in a repository, newest first, optionally on one branch. If hasMore is true, pass nextPage as page for the next batch.")]
    public async Task<JsonNode> ListCommits(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("Optional branch name. Defaults to the repository's main branch.")] string? branch = null,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Most items per page, from 1 to 100. Defaults to 25.")] int pagelen = 25,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var (count, number) = Paging(page, pagelen);
        var tail = string.IsNullOrWhiteSpace(branch) ? string.Empty : "/" + Uri.EscapeDataString(Names.Ref(branch));

        var response = await bitbucket.GetAsync(
            UpstreamClient.WithQuery(
                $"repositories/{ws}/{repo}/commits{tail}",
                ("pagelen", count.ToString()),
                ("page", number.ToString())),
            cancellationToken);

        return Paged(response, "commits", count, number, c => Shape.Obj(
            ("hash", Shape.Str(c, "hash")),
            ("date", Shape.Str(c, "date")),
            ("author", Shape.Str(c, "author", "user", "display_name") ?? Shape.Str(c, "author", "raw")),
            ("message", Shape.Cap(Shape.Str(c, "message"), 500, out _)?.Trim()),
            ("url", Shape.Str(c, "links", "html", "href"))));
    }

    [McpServerTool(Name = "bitbucket_get_file", Title = "Get Bitbucket File", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Reads one text file from a repository at a branch, tag, or commit. Large files are cut at a size limit and say so. Binary files are refused. Use it to read source, configuration, or documentation.")]
    public async Task<JsonNode> GetFile(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("The branch, tag, or commit hash to read from, such as main.")] string @ref,
        [Description("The file path inside the repository, such as src/app/Program.cs.")] string path,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var reference = Uri.EscapeDataString(Names.Ref(@ref));
        var filePath = Names.FilePath(path);

        var content = await bitbucket.TextAsync($"repositories/{ws}/{repo}/src/{reference}/{filePath}", cancellationToken);
        if (content.Contains('\0'))
        {
            throw new McpException("That file is binary, so it cannot be shown as text.");
        }

        var text = Shape.Cap(content, limits.Value.MaxTextChars, out var truncated);
        var result = Shape.Obj(("path", Uri.UnescapeDataString(filePath)), ("ref", @ref), ("content", text), ("characters", content.Length));
        if (truncated)
        {
            result["truncated"] = true;
            result["note"] = "The file is longer than the size limit, so the end was left out.";
        }

        return result;
    }

    private (string Workspace, string Repository) Target(string workspace, string repository) =>
        (bitbucket.Workspace(workspace), Names.Slug(repository, "repository"));

    private (int Count, int Page) Paging(int page, int pagelen) =>
        (Shape.Clamp(pagelen, 1, Math.Min(100, limits.Value.MaxItems)), Math.Max(1, page));

    private JsonNode Paged(JsonNode? response, string name, int count, int page, Func<JsonNode, JsonObject> map)
    {
        var (items, truncated) = Shape.Limit(
            Shape.Items(response, "values").Select(i => (JsonNode?)map(i)), count, limits.Value.MaxResponseChars);

        var hasMore = Shape.At(response, "next") is not null || truncated;
        var result = Shape.Obj(("returned", items.Count), ("page", page), (name, items), ("hasMore", hasMore));
        if (hasMore && !truncated)
        {
            result["nextPage"] = page + 1;
        }

        if (truncated)
        {
            result["note"] = "Some items were left out to keep the response small. Request a smaller pagelen.";
        }

        return result;
    }

    private static JsonObject Repository(JsonNode r) => Shape.Obj(
        ("fullName", Shape.Str(r, "full_name")),
        ("name", Shape.Str(r, "name")),
        ("slug", Shape.Str(r, "slug")),
        ("project", Shape.Str(r, "project", "name")),
        ("description", Shape.Cap(Shape.Str(r, "description"), 1000, out _)),
        ("mainBranch", Shape.Str(r, "mainbranch", "name")),
        ("language", Shape.Str(r, "language")),
        ("isPrivate", Shape.Bool(r, "is_private")),
        ("updated", Shape.Str(r, "updated_on")),
        ("url", Shape.Str(r, "links", "html", "href")));

    private JsonObject PullRequest(JsonNode p, bool full)
    {
        var result = Shape.Obj(
            ("id", Shape.Int(p, "id")),
            ("title", Shape.Str(p, "title")),
            ("state", Shape.Str(p, "state")),
            ("author", Shape.Str(p, "author", "display_name")),
            ("sourceBranch", Shape.Str(p, "source", "branch", "name")),
            ("destinationBranch", Shape.Str(p, "destination", "branch", "name")),
            ("created", Shape.Str(p, "created_on")),
            ("updated", Shape.Str(p, "updated_on")),
            ("commentCount", Shape.Int(p, "comment_count")),
            ("url", Shape.Str(p, "links", "html", "href")));

        if (full)
        {
            result["description"] = Shape.Cap(Shape.Str(p, "description"), limits.Value.MaxTextChars, out _);
            result["closeSourceBranch"] = Shape.Bool(p, "close_source_branch");
            result["mergeCommit"] = Shape.Str(p, "merge_commit", "hash");
            result["reviewers"] = new JsonArray(Shape.Items(p, "participants")
                .Where(x => Shape.Str(x, "role") == "REVIEWER")
                .Select(x => (JsonNode?)Shape.Obj(
                    ("name", Shape.Str(x, "user", "display_name")),
                    ("approved", Shape.Bool(x, "approved")),
                    ("state", Shape.Str(x, "state"))))
                .ToArray());
        }

        return result;
    }
}
