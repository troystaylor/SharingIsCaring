using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Atlassian.Mcp.Core;

namespace Atlassian.Mcp.Jira;

/// <summary>
/// Turns Atlassian Document Format, the JSON tree Jira uses for descriptions and comments, into readable
/// plain text. Structure a model needs (headings, lists, code, links, mentions, tables) is kept; styling is not.
/// </summary>
public static partial class Adf
{
    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();

    /// <summary>
    /// Builds an ADF document from the plain text a model writes: paragraphs, headings, bullet and numbered
    /// lists, and fenced code. Jira rejects empty text nodes, so empty lines produce no node.
    /// </summary>
    public static JsonObject FromText(string? text)
    {
        var content = new JsonArray();

        foreach (var block in TextBlocks.Parse(text))
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    var inline = new JsonArray();
                    for (var i = 0; i < paragraph.Lines.Count; i++)
                    {
                        if (i > 0)
                        {
                            inline.Add(new JsonObject { ["type"] = "hardBreak" });
                        }

                        inline.Add(TextNode(paragraph.Lines[i]));
                    }

                    content.Add(new JsonObject { ["type"] = "paragraph", ["content"] = inline });
                    break;

                case HeadingBlock heading:
                    content.Add(new JsonObject
                    {
                        ["type"] = "heading",
                        ["attrs"] = new JsonObject { ["level"] = heading.Level },
                        ["content"] = new JsonArray(TextNode(heading.Text))
                    });
                    break;

                case ListBlock list:
                    var items = new JsonArray();
                    foreach (var item in list.Items)
                    {
                        items.Add(new JsonObject
                        {
                            ["type"] = "listItem",
                            ["content"] = new JsonArray(new JsonObject
                            {
                                ["type"] = "paragraph",
                                ["content"] = new JsonArray(TextNode(item))
                            })
                        });
                    }

                    content.Add(new JsonObject
                    {
                        ["type"] = list.Ordered ? "orderedList" : "bulletList",
                        ["content"] = items
                    });
                    break;

                case CodeBlock code:
                    var node = new JsonObject { ["type"] = "codeBlock" };
                    if (code.Language.Length > 0)
                    {
                        node["attrs"] = new JsonObject { ["language"] = code.Language };
                    }

                    if (code.Code.Length > 0)
                    {
                        node["content"] = new JsonArray(TextNode(code.Code));
                    }

                    content.Add(node);
                    break;
            }
        }

        return new JsonObject { ["version"] = 1, ["type"] = "doc", ["content"] = content };
    }

    private static JsonObject TextNode(string text) => new() { ["type"] = "text", ["text"] = text };

    public static string ToText(JsonNode? document, int maxChars = 30_000)
    {
        var sb = new StringBuilder();
        Render(document, sb);

        var text = BlankRuns().Replace(sb.ToString().Replace("\r", string.Empty), "\n\n").Trim();
        return text.Length > maxChars ? text[..maxChars] + "..." : text;
    }

    private static void Render(JsonNode? node, StringBuilder sb)
    {
        if (node is not JsonObject)
        {
            return;
        }

        switch (Shape.Str(node, "type"))
        {
            case "text":
                sb.Append(Shape.Str(node, "text"));
                AppendLink(node, sb);
                break;

            case "hardBreak":
                sb.Append('\n');
                break;

            case "paragraph":
                Children(node, sb);
                sb.Append("\n\n");
                break;

            case "heading":
                sb.Append('#', Math.Clamp(Shape.Int(node, "attrs", "level") ?? 1, 1, 6)).Append(' ');
                Children(node, sb);
                sb.Append("\n\n");
                break;

            case "bulletList":
            case "orderedList":
                List(node, sb, Shape.Str(node, "type") == "orderedList");
                break;

            case "taskList":
                foreach (var item in Shape.Items(node, "content"))
                {
                    var done = Shape.Str(item, "attrs", "state") == "DONE";
                    sb.Append(done ? "[x] " : "[ ] ");
                    Children(item, sb);
                    sb.Append('\n');
                }

                sb.Append('\n');
                break;

            case "codeBlock":
                sb.Append("```").Append(Shape.Str(node, "attrs", "language")).Append('\n');
                Children(node, sb);
                sb.Append("\n```\n\n");
                break;

            case "blockquote":
                var quote = new StringBuilder();
                Children(node, quote);
                foreach (var line in quote.ToString().Trim().Split('\n'))
                {
                    sb.Append("> ").Append(line).Append('\n');
                }

                sb.Append('\n');
                break;

            case "rule":
                sb.Append("---\n\n");
                break;

            case "mention":
                sb.Append(Shape.Str(node, "attrs", "text") ?? "@someone");
                break;

            case "emoji":
                sb.Append(Shape.Str(node, "attrs", "text") ?? Shape.Str(node, "attrs", "shortName"));
                break;

            case "inlineCard":
            case "blockCard":
            case "embedCard":
                sb.Append(Shape.Str(node, "attrs", "url"));
                if (Shape.Str(node, "type") != "inlineCard")
                {
                    sb.Append("\n\n");
                }

                break;

            case "status":
                sb.Append('[').Append(Shape.Str(node, "attrs", "text")).Append(']');
                break;

            case "date":
                sb.Append(
                    long.TryParse(Shape.Str(node, "attrs", "timestamp"), out var ms)
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToString("yyyy-MM-dd")
                        : string.Empty);
                break;

            case "media":
            case "mediaInline":
                sb.Append("[attachment]");
                break;

            case "mediaSingle":
            case "mediaGroup":
                Children(node, sb);
                sb.Append("\n\n");
                break;

            case "table":
                foreach (var row in Shape.Items(node, "content"))
                {
                    var cells = Shape.Items(row, "content").Select(CellText);
                    sb.Append(string.Join(" | ", cells)).Append('\n');
                }

                sb.Append('\n');
                break;

            case "expand":
            case "nestedExpand":
                if (Shape.Str(node, "attrs", "title") is { Length: > 0 } title)
                {
                    sb.Append(title).Append('\n');
                }

                Children(node, sb);
                break;

            default:
                // panel, listItem, tableCell, and any node a newer Jira adds: show what is inside.
                Children(node, sb);
                break;
        }
    }

    private static void Children(JsonNode? node, StringBuilder sb)
    {
        foreach (var child in Shape.Items(node, "content"))
        {
            Render(child, sb);
        }
    }

    private static void List(JsonNode list, StringBuilder sb, bool ordered)
    {
        var number = 1;
        foreach (var item in Shape.Items(list, "content"))
        {
            var inner = new StringBuilder();
            Children(item, inner);

            var lines = inner.ToString().Trim().Split('\n');
            sb.Append(ordered ? $"{number++}. " : "- ").Append(lines[0]).Append('\n');
            foreach (var line in lines.Skip(1))
            {
                sb.Append("  ").Append(line).Append('\n');
            }
        }

        sb.Append('\n');
    }

    private static string CellText(JsonNode cell)
    {
        var inner = new StringBuilder();
        Children(cell, inner);
        return inner.ToString().Trim().Replace('\n', ' ');
    }

    private static void AppendLink(JsonNode textNode, StringBuilder sb)
    {
        foreach (var mark in Shape.Items(textNode, "marks"))
        {
            if (Shape.Str(mark, "type") == "link" && Shape.Str(mark, "attrs", "href") is { } href)
            {
                sb.Append(" (").Append(href).Append(')');
            }
        }
    }
}
