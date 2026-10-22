using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace Atlassian.Mcp.Core;

/// <summary>
/// Validates every identifier a model supplies before it reaches a URL or a query. Tool arguments are untrusted:
/// each must match the shape the provider uses, so a model cannot steer a call to another path.
/// </summary>
public static partial class Names
{
    public const int MaxQueryChars = 2000;
    public const int MaxFieldCount = 30;

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{1,19}-\d{1,9}\z")]
    private static partial Regex IssueKeyPattern();

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{1,19}\z")]
    private static partial Regex ProjectKeyPattern();

    [GeneratedRegex(@"^[A-Za-z0-9~][A-Za-z0-9_~.-]{0,254}\z")]
    private static partial Regex SpaceKeyPattern();

    [GeneratedRegex(@"^\d{1,20}\z")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._/@+-]{1,200}\z")]
    private static partial Regex RefPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}\z")]
    private static partial Regex FieldPattern();

    [GeneratedRegex(@"^[A-Za-z0-9+/=_.:~-]{1,2000}\z")]
    private static partial Regex CursorPattern();

    [GeneratedRegex(@"^[A-Za-z0-9 ._-]{1,100}\z")]
    private static partial Regex SafeTextPattern();

    [GeneratedRegex(@"^[0-9a-fA-F]{4,64}\z")]
    private static partial Regex HashPattern();

    [GeneratedRegex(@"^[A-Za-z0-9:_-]{1,128}\z")]
    private static partial Regex AccountIdPattern();

    [GeneratedRegex(@"^\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}\z")]
    private static partial Regex UuidPattern();

    [GeneratedRegex(@"^\S{1,255}\z")]
    private static partial Regex LabelPattern();

    /// <summary>An Atlassian account ID, such as 5b10ac8d82e05b22cc7d4ef5 or 712020:0f1e2d3c-....</summary>
    public static string AccountId(string? value)
    {
        var id = (value ?? string.Empty).Trim();
        return AccountIdPattern().IsMatch(id)
            ? id
            : throw new McpException("The account ID must be an Atlassian account ID. Find one with jira_search_users.");
    }

    /// <summary>A Bitbucket user UUID in braces, such as {1a2b3c4d-....}.</summary>
    public static string Uuid(string? value)
    {
        var uuid = (value ?? string.Empty).Trim();
        return UuidPattern().IsMatch(uuid)
            ? uuid
            : throw new McpException("A user UUID must look like {1a2b3c4d-1a2b-1a2b-1a2b-1a2b3c4d5e6f}, braces included.");
    }

    /// <summary>A Jira label. Labels cannot contain spaces.</summary>
    public static string Label(string? value)
    {
        var label = (value ?? string.Empty).Trim();
        return LabelPattern().IsMatch(label) && !label.Any(char.IsControl)
            ? label
            : throw new McpException("A label must be 1 to 255 characters with no spaces.");
    }

    /// <summary>A single line of text, such as a title or summary.</summary>
    public static string Line(string? value, string what, int max = 255)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new McpException($"The {what} is empty.");
        }

        if (text.Length > max)
        {
            throw new McpException($"The {what} is longer than {max} characters.");
        }

        return text.Any(char.IsControl)
            ? throw new McpException($"The {what} must be a single line of text.")
            : text;
    }

    /// <summary>Multi-line text for a description, comment, or page body. Newlines and tabs are allowed.</summary>
    public static string Body(string? value, string what, int max)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new McpException($"The {what} is empty.");
        }

        if (text.Length > max)
        {
            throw new McpException($"The {what} is longer than {max} characters.");
        }

        return text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
            ? throw new McpException($"The {what} contains control characters.")
            : text;
    }

    /// <summary>A Jira issue key such as PROJ-123. Lower case is accepted and normalised.</summary>
    public static string IssueKey(string? value)
    {
        var key = (value ?? string.Empty).Trim().ToUpperInvariant();
        return IssueKeyPattern().IsMatch(key)
            ? key
            : throw new McpException("The issue key must look like PROJ-123.");
    }

    public static string ProjectKey(string? value)
    {
        var key = (value ?? string.Empty).Trim().ToUpperInvariant();
        return ProjectKeyPattern().IsMatch(key)
            ? key
            : throw new McpException("The project key must be capital letters, digits, or underscores, such as PROJ.");
    }

    /// <summary>A Confluence space key. Personal spaces start with a tilde.</summary>
    public static string SpaceKey(string? value)
    {
        var key = (value ?? string.Empty).Trim();
        return SpaceKeyPattern().IsMatch(key)
            ? key
            : throw new McpException("The space key may contain only letters, digits, dots, hyphens, underscores, and a leading tilde.");
    }

    /// <summary>A numeric ID, such as a Confluence page ID or a pull request number.</summary>
    public static string Number(string? value, string what)
    {
        var id = (value ?? string.Empty).Trim();
        return NumberPattern().IsMatch(id) ? id : throw new McpException($"The {what} must be a number.");
    }

    /// <summary>A Bitbucket workspace or repository slug.</summary>
    public static string Slug(string? value, string what)
    {
        var slug = (value ?? string.Empty).Trim();
        return SlugPattern().IsMatch(slug)
            ? slug
            : throw new McpException($"The {what} must be a slug of letters, digits, dots, hyphens, or underscores.");
    }

    /// <summary>A branch, tag, or commit reference.</summary>
    public static string Ref(string? value)
    {
        var reference = (value ?? string.Empty).Trim();
        return RefPattern().IsMatch(reference) && !reference.Contains("..") && !reference.StartsWith('/')
            ? reference
            : throw new McpException("The ref must be a branch name, tag, or commit hash.");
    }

    public static string Hash(string? value)
    {
        var hash = (value ?? string.Empty).Trim();
        return HashPattern().IsMatch(hash) ? hash : throw new McpException("The commit must be a hex hash.");
    }

    /// <summary>A repository file path, normalised and checked. Segments are not escaped.</summary>
    public static string RepoPath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Trim('/');
        if (path.Length is 0 or > 500 || path.Any(char.IsControl))
        {
            throw new McpException("The path must be a repository file path of up to 500 characters.");
        }

        var segments = path.Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".."))
        {
            throw new McpException("The path must not contain empty, '.', or '..' segments.");
        }

        return path;
    }

    /// <summary>A repository file path, with each segment escaped and ready to put in a URL.</summary>
    public static string FilePath(string? value) =>
        string.Join('/', RepoPath(value).Split('/').Select(Uri.EscapeDataString));

    /// <summary>A JQL or CQL statement. Length-limited and free of control characters; the provider parses it.</summary>
    public static string Query(string? value, string language)
    {
        var query = (value ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            throw new McpException($"The {language} query is empty.");
        }

        if (query.Length > MaxQueryChars)
        {
            throw new McpException($"The {language} query is longer than {MaxQueryChars} characters.");
        }

        if (query.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
        {
            throw new McpException($"The {language} query contains control characters.");
        }

        return query;
    }

    public static string Field(string? value) =>
        value is not null && FieldPattern().IsMatch(value)
            ? value
            : throw new McpException("A field name may contain only letters, digits, dots, hyphens, and underscores.");

    public static IReadOnlyList<string> Fields(string[]? values)
    {
        if (values is null or { Length: 0 })
        {
            return [];
        }

        if (values.Length > MaxFieldCount)
        {
            throw new McpException($"Request at most {MaxFieldCount} fields.");
        }

        return values.Select(Field).ToArray();
    }

    /// <summary>An opaque pagination token returned by a previous call. A URL is never accepted in its place.</summary>
    public static string Cursor(string? value) =>
        value is not null && CursorPattern().IsMatch(value) && !value.Contains("://")
            ? value
            : throw new McpException("The cursor must be the value returned by the previous call, unchanged.");

    /// <summary>Text for a name filter. Restricted so it can sit inside a quoted query value.</summary>
    public static string SafeText(string? value, string what) =>
        value is not null && SafeTextPattern().IsMatch(value.Trim())
            ? value.Trim()
            : throw new McpException($"The {what} may contain only letters, digits, spaces, dots, hyphens, and underscores.");
}
