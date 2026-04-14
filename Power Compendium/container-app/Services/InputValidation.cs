using System.Text.RegularExpressions;

namespace LLMbook.Api.Services;

/// <summary>
/// Input validation and sanitization for book operations.
/// </summary>
public static partial class InputValidation
{
    // PageId: kebab-case only, 1-128 chars, no path separators or special characters
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,126}[a-z0-9])?$")]
    private static partial Regex PageIdPattern();

    public const int MaxContentLength = 100 * 1024;    // 100 KB for ingest/write content
    public const int MaxQuestionLength = 1024;           // 1 KB for questions
    public const int MaxTitleLength = 256;               // 256 chars for titles

    /// <summary>
    /// Validate a pageId is safe kebab-case with no path traversal risk.
    /// </summary>
    public static bool IsValidPageId(string? pageId, out string error)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            error = "pageId is required";
            return false;
        }

        if (pageId.Length > 128)
        {
            error = "pageId must be 128 characters or fewer";
            return false;
        }

        // Block any path separators or traversal attempts
        if (pageId.Contains('/') || pageId.Contains('\\') || pageId.Contains(".."))
        {
            error = "pageId must not contain path separators or '..'";
            return false;
        }

        if (!PageIdPattern().IsMatch(pageId))
        {
            error = "pageId must be kebab-case: lowercase letters, numbers, and hyphens only (e.g., 'azure-functions-overview')";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Validate content length is within limits.
    /// </summary>
    public static bool IsWithinContentLimit(string? content, out string error)
    {
        if (content != null && content.Length > MaxContentLength)
        {
            error = $"Content exceeds maximum length of {MaxContentLength / 1024} KB";
            return false;
        }
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Validate question length is within limits.
    /// </summary>
    public static bool IsWithinQuestionLimit(string? question, out string error)
    {
        if (question != null && question.Length > MaxQuestionLength)
        {
            error = $"Question exceeds maximum length of {MaxQuestionLength} characters";
            return false;
        }
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Sanitize text for safe inclusion in markdown log entries.
    /// Strips markdown heading markers and trims to a max length.
    /// </summary>
    public static string SanitizeForLog(string? input, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(input))
            return "(empty)";

        // Remove markdown heading markers, links, and control characters
        var sanitized = input
            .Replace("#", "")
            .Replace("[", "")
            .Replace("]", "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength] + "...";

        return sanitized;
    }
}
