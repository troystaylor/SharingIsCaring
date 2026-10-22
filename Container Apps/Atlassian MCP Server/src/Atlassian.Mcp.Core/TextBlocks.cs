using System.Text.RegularExpressions;

namespace Atlassian.Mcp.Core;

public abstract record Block;

public sealed record ParagraphBlock(IReadOnlyList<string> Lines) : Block;

public sealed record HeadingBlock(int Level, string Text) : Block;

public sealed record ListBlock(bool Ordered, IReadOnlyList<string> Items) : Block;

public sealed record CodeBlock(string Language, string Code) : Block;

/// <summary>
/// Reads the plain text a model writes into blocks: paragraphs, headings (<c>#</c>), bullet and numbered
/// lists, and fenced code. Jira and Confluence each turn these blocks into their own rich-text format, so a
/// model never has to produce Atlassian Document Format or storage XHTML.
/// </summary>
public static partial class TextBlocks
{
    [GeneratedRegex(@"^\s*```\s*([A-Za-z0-9+#._-]*)\s*$")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*[-*]\s+(.+?)\s*$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*\d{1,3}[.)]\s+(.+?)\s*$")]
    private static partial Regex Numbered();

    public static IReadOnlyList<Block> Parse(string? text)
    {
        var blocks = new List<Block>();
        var paragraph = new List<string>();
        var items = new List<string>();
        var ordered = false;

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new ParagraphBlock(paragraph.ToArray()));
                paragraph.Clear();
            }
        }

        void FlushList()
        {
            if (items.Count > 0)
            {
                blocks.Add(new ListBlock(ordered, items.ToArray()));
                items.Clear();
            }
        }

        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (Fence().Match(line) is { Success: true } fence)
            {
                FlushParagraph();
                FlushList();

                // An unclosed fence runs to the end, so a model that forgets the closing line still gets code.
                var code = new List<string>();
                for (i++; i < lines.Length && !Fence().IsMatch(lines[i]); i++)
                {
                    code.Add(lines[i]);
                }

                blocks.Add(new CodeBlock(fence.Groups[1].Value, string.Join('\n', code)));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            if (Heading().Match(line) is { Success: true } heading)
            {
                FlushParagraph();
                FlushList();
                blocks.Add(new HeadingBlock(heading.Groups[1].Length, heading.Groups[2].Value));
                continue;
            }

            var bullet = Bullet().Match(line);
            var numbered = bullet.Success ? Match.Empty : Numbered().Match(line);
            if (bullet.Success || numbered.Success)
            {
                var isOrdered = numbered.Success;
                FlushParagraph();
                if (items.Count > 0 && ordered != isOrdered)
                {
                    FlushList();
                }

                ordered = isOrdered;
                items.Add((bullet.Success ? bullet : numbered).Groups[1].Value);
                continue;
            }

            FlushList();
            paragraph.Add(line.Trim());
        }

        FlushParagraph();
        FlushList();
        return blocks;
    }
}
