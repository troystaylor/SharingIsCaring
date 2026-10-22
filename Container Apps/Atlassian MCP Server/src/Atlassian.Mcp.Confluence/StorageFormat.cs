using System.Net;
using System.Text.RegularExpressions;
using Atlassian.Mcp.Core;

namespace Atlassian.Mcp.Confluence;

/// <summary>
/// Turns Confluence storage format, the XHTML-based markup behind a page, into readable plain text. Headings,
/// lists, code, tables, and page links are kept as simple text; macros are named so a reader knows something
/// dynamic was there.
/// </summary>
public static partial class StorageFormat
{
    [GeneratedRegex(@"<!\[CDATA\[(.*?)\]\]>", RegexOptions.Singleline)]
    private static partial Regex Cdata();

    [GeneratedRegex(@"<ri:page[^>]*ri:content-title=""([^""]*)""[^>]*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex PageLink();

    [GeneratedRegex(@"<ac:structured-macro[^>]*ac:name=""([^""]+)""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Macro();

    [GeneratedRegex(@"<h([1-6])[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingOpen();

    [GeneratedRegex(@"</(p|h[1-6]|div|pre|blockquote|ul|ol|table)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockClose();

    [GeneratedRegex(@"<br\s*/?>|</li>|</tr>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"</t[dh]>", RegexOptions.IgnoreCase)]
    private static partial Regex CellClose();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();

    /// <summary>
    /// Builds storage format from the plain text a model writes: paragraphs, headings, bullet and numbered
    /// lists, and fenced code (as a code macro). All text is escaped, so a model cannot inject markup or macros.
    /// </summary>
    public static string FromText(string? text)
    {
        var html = new List<string>();

        foreach (var block in TextBlocks.Parse(text))
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    html.Add("<p>" + string.Join("<br />", paragraph.Lines.Select(WebUtility.HtmlEncode)) + "</p>");
                    break;

                case HeadingBlock heading:
                    html.Add($"<h{heading.Level}>{WebUtility.HtmlEncode(heading.Text)}</h{heading.Level}>");
                    break;

                case ListBlock list:
                    var tag = list.Ordered ? "ol" : "ul";
                    html.Add($"<{tag}>" + string.Concat(list.Items.Select(i => $"<li>{WebUtility.HtmlEncode(i)}</li>")) + $"</{tag}>");
                    break;

                case CodeBlock code:
                    var language = code.Language.Length > 0
                        ? $"<ac:parameter ac:name=\"language\">{WebUtility.HtmlEncode(code.Language)}</ac:parameter>"
                        : string.Empty;

                    // A CDATA section ends at the first ]]>, so split any that appear in the code.
                    var body = code.Code.Replace("]]>", "]]]]><![CDATA[>");
                    html.Add(
                        "<ac:structured-macro ac:name=\"code\">" + language
                        + $"<ac:plain-text-body><![CDATA[{body}]]></ac:plain-text-body></ac:structured-macro>");
                    break;
            }
        }

        return string.Concat(html);
    }

    public static string ToText(string? storage, int maxChars = 30_000)
    {
        if (string.IsNullOrWhiteSpace(storage))
        {
            return string.Empty;
        }

        var text = Cdata().Replace(storage, m => "\n" + WebUtility.HtmlEncode(m.Groups[1].Value) + "\n");
        text = PageLink().Replace(text, "[$1]");
        text = Macro().Replace(text, "[macro:$1] ");
        text = HeadingOpen().Replace(text, m => "\n" + new string('#', int.Parse(m.Groups[1].Value)) + " ");
        text = ListItem().Replace(text, "- ");
        text = CellClose().Replace(text, " | ");
        text = BlockClose().Replace(text, "\n\n");
        text = LineBreak().Replace(text, "\n");
        text = AnyTag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = TrailingSpace().Replace(text, "\n");
        text = BlankRuns().Replace(text, "\n\n").Trim();

        return text.Length > maxChars ? text[..maxChars] + "..." : text;
    }
}
