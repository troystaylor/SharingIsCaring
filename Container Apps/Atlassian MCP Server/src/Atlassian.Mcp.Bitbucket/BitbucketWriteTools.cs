using System.ComponentModel;
using System.Text.Json.Nodes;
using Atlassian.Mcp.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Atlassian.Mcp.Bitbucket;

/// <summary>
/// Tools that create or change Bitbucket data. They are registered only when <c>Bitbucket:EnableWriteTools</c>
/// is true. Each call runs as the signed-in person, so Bitbucket attributes the change to them and enforces their
/// repository permissions. Merging and deleting are deliberately not offered.
/// </summary>
[McpServerToolType]
public sealed class BitbucketWriteTools(BitbucketClient bitbucket, IOptions<LimitOptions> limits)
{
    private const int MaxReviewers = 10;

    [McpServerTool(Name = "bitbucket_create_pull_request_comment", Title = "Comment on Pull Request", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Adds a comment to a pull request as the signed-in user. Pass path and line to comment on a line of the changed file, or omit both for a general comment. Pass replyTo with a comment ID from bitbucket_list_pull_request_comments to reply in a thread. This is not idempotent: a repeated call adds a second comment, so list the comments before retrying after a failure.")]
    public async Task<JsonNode> CreatePullRequestComment(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("The pull request number.")] string pullRequestId,
        [Description("The comment text. Markdown is supported.")] string comment,
        [Description("Optional file path in the pull request, for an inline comment. Requires line.")] string? path = null,
        [Description("Optional line number in the new version of the file. Requires path.")] int? line = null,
        [Description("Optional ID of the comment to reply to.")] int? replyTo = null,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var id = Names.Number(pullRequestId, "pull request number");

        if ((path is null) != (line is null))
        {
            throw new McpException("An inline comment needs both path and line. Omit both for a general comment.");
        }

        var request = new JsonObject
        {
            ["content"] = new JsonObject { ["raw"] = Names.Body(comment, "comment", limits.Value.MaxTextChars) }
        };

        if (path is not null)
        {
            if (line < 1)
            {
                throw new McpException("The line must be 1 or more.");
            }

            request["inline"] = new JsonObject { ["path"] = Names.RepoPath(path), ["to"] = line };
        }

        if (replyTo is not null)
        {
            if (replyTo < 1)
            {
                throw new McpException("The replyTo comment ID must be 1 or more.");
            }

            request["parent"] = new JsonObject { ["id"] = replyTo };
        }

        var response = await bitbucket.SendAsync(
            HttpMethod.Post, $"repositories/{ws}/{repo}/pullrequests/{id}/comments", request, cancellationToken);

        return Shape.Obj(
            ("added", true),
            ("id", Shape.Int(response, "id")),
            ("author", Shape.Str(response, "user", "display_name")),
            ("url", Shape.Str(response, "links", "html", "href")));
    }

    [McpServerTool(Name = "bitbucket_create_pull_request", Title = "Create Pull Request", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Opens a pull request from a branch that already exists, as the signed-in user, and returns its number and url. The source branch must already be pushed. Omit destinationBranch to target the repository's main branch. This is not idempotent: if the call fails or you are unsure it worked, check bitbucket_list_pull_requests before trying again, or you will open a duplicate. Confirm the branches and title with the user first.")]
    public async Task<JsonNode> CreatePullRequest(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("The pull request title.")] string title,
        [Description("The branch with the changes.")] string sourceBranch,
        [Description("Optional branch to merge into. Defaults to the repository's main branch.")] string? destinationBranch = null,
        [Description("Optional description. Markdown is supported.")] string? description = null,
        [Description("Optional. When true, the source branch is deleted when the pull request is merged.")] bool closeSourceBranch = false,
        [Description("Optional reviewers' user UUIDs, braces included.")] string[]? reviewerUuids = null,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);

        var request = new JsonObject
        {
            ["title"] = Names.Line(title, "title"),
            ["source"] = new JsonObject { ["branch"] = new JsonObject { ["name"] = Names.Ref(sourceBranch) } },
            ["close_source_branch"] = closeSourceBranch
        };

        if (!string.IsNullOrWhiteSpace(destinationBranch))
        {
            request["destination"] = new JsonObject { ["branch"] = new JsonObject { ["name"] = Names.Ref(destinationBranch) } };
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            request["description"] = Names.Body(description, "description", limits.Value.MaxTextChars);
        }

        if (reviewerUuids is { Length: > 0 })
        {
            if (reviewerUuids.Length > MaxReviewers)
            {
                throw new McpException($"Add at most {MaxReviewers} reviewers.");
            }

            request["reviewers"] = new JsonArray(reviewerUuids
                .Select(u => (JsonNode?)new JsonObject { ["uuid"] = Names.Uuid(u) }).ToArray());
        }

        var response = await bitbucket.SendAsync(
            HttpMethod.Post, $"repositories/{ws}/{repo}/pullrequests", request, cancellationToken);

        return Shape.Obj(
            ("created", true),
            ("id", Shape.Int(response, "id")),
            ("title", Shape.Str(response, "title")),
            ("state", Shape.Str(response, "state")),
            ("sourceBranch", Shape.Str(response, "source", "branch", "name")),
            ("destinationBranch", Shape.Str(response, "destination", "branch", "name")),
            ("url", Shape.Str(response, "links", "html", "href")));
    }

    [McpServerTool(Name = "bitbucket_approve_pull_request", Title = "Approve Pull Request", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Approves a pull request as the signed-in user. Approving again has no further effect. Only approve after reading the change; use bitbucket_get_pull_request_diff, and confirm with the user. This does not merge the pull request.")]
    public async Task<JsonNode> ApprovePullRequest(
        [Description("The workspace slug.")] string workspace,
        [Description("The repository slug.")] string repository,
        [Description("The pull request number.")] string pullRequestId,
        CancellationToken cancellationToken = default)
    {
        var (ws, repo) = Target(workspace, repository);
        var id = Names.Number(pullRequestId, "pull request number");

        var response = await bitbucket.SendAsync(
            HttpMethod.Post, $"repositories/{ws}/{repo}/pullrequests/{id}/approve", null, cancellationToken);

        return Shape.Obj(
            ("approved", Shape.Bool(response, "approved") ?? true),
            ("pullRequest", id),
            ("by", Shape.Str(response, "user", "display_name")));
    }

    private (string Workspace, string Repository) Target(string workspace, string repository) =>
        (bitbucket.Workspace(workspace), Names.Slug(repository, "repository"));
}
