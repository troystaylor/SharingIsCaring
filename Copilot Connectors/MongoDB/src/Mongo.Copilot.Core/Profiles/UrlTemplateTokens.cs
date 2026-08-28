using System.Text.RegularExpressions;

namespace Mongo.Copilot.Core.Profiles;

/// <summary>
/// Shared parsing for <c>{field}</c> placeholders in URL templates. Used by both the
/// profile (to declare referenced fields) and the citation resolver (to substitute them).
/// </summary>
public static partial class UrlTemplateTokens
{
    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    public static Regex Pattern => TokenRegex();

    public static IEnumerable<string> Extract(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            yield break;

        foreach (Match match in TokenRegex().Matches(template))
            yield return match.Groups[1].Value;
    }
}
