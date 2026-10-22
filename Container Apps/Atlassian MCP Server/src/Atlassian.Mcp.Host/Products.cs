using Atlassian.Mcp.Core;

namespace Atlassian.Mcp.Host;

/// <summary>One MCP endpoint: where it lives, which tools it serves, and how callers sign in to it.</summary>
public sealed record Product(
    string Key,
    string Path,
    string ToolPrefix,
    string Title,
    string Instructions,
    string AuthorizationServer,
    string[] Scopes,
    string[] WriteScopes,
    string AuthScheme);

public static class Products
{
    public static readonly Product Jira = new(
        "jira",
        "/jira/mcp",
        "jira_",
        "Jira MCP Server",
        "Reads Jira Cloud through the Jira REST API, as the Atlassian user who is signed in, so results are limited " +
        "to what that person can see. Find issues with jira_search_issues using JQL that includes a restriction " +
        "such as a project or assignee. If you do not know a project key, call jira_list_projects first. Read one " +
        "issue in full with jira_get_issue. Every issue includes a url you can cite. An empty list means nothing " +
        "matched, not that the call failed.",
        "https://auth.atlassian.com",
        ["read:jira-work", "read:jira-user", "read:me", "offline_access"],
        ["write:jira-work"],
        TokenSchemes.Atlassian);

    public static readonly Product Confluence = new(
        "confluence",
        "/confluence/mcp",
        "confluence_",
        "Confluence MCP Server",
        "Reads Confluence Cloud through the Confluence REST API, as the Atlassian user who is signed in, so results " +
        "are limited to the spaces and pages that person can see. Find pages with confluence_search_content using " +
        "CQL, then read one with confluence_get_page. Browse with confluence_list_spaces and confluence_list_pages. " +
        "Every result includes a url you can cite. An empty list means nothing matched, not that the call failed.",
        "https://auth.atlassian.com",
        ["read:page:confluence", "read:space:confluence", "search:confluence", "read:me", "offline_access"],
        ["write:page:confluence", "write:comment:confluence"],
        TokenSchemes.Atlassian);

    public static readonly Product Bitbucket = new(
        "bitbucket",
        "/bitbucket/mcp",
        "bitbucket_",
        "Bitbucket MCP Server",
        "Reads Bitbucket Cloud through the Bitbucket REST API, as the Bitbucket user who is signed in, so results " +
        "are limited to the workspaces and repositories that person can see. Find a workspace with " +
        "bitbucket_list_workspaces, then repositories, then pull requests. Check how large a pull request is with " +
        "bitbucket_get_pull_request_diffstat before reading bitbucket_get_pull_request_diff. Lists are paged by " +
        "page number. Every result includes a url you can cite.",
        "https://bitbucket.org/site/oauth2",
        ["account", "repository", "pullrequest"],
        ["pullrequest:write"],
        TokenSchemes.Bitbucket);

    public static IReadOnlyList<Product> All { get; } = [Jira, Confluence, Bitbucket];

    /// <summary>The product whose endpoint <paramref name="path"/> belongs to, or null.</summary>
    public static Product? ForPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var product in All)
        {
            var root = "/" + product.Key;
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                return product;
            }
        }

        return null;
    }
}
