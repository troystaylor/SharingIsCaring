using System.Text.Json.Nodes;
using Atlassian.Mcp.Confluence;
using Atlassian.Mcp.Core;
using Atlassian.Mcp.Jira;
using ModelContextProtocol;

namespace Atlassian.Mcp.Tests;

public class ConverterTests
{
    private static string Adf(string json) => Atlassian.Mcp.Jira.Adf.ToText(JsonNode.Parse(json));

    [Fact]
    public void Adf_renders_headings_lists_code_quotes_and_rules()
    {
        var text = Adf("""
            {"type":"doc","content":[
              {"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Title"}]},
              {"type":"orderedList","content":[
                {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"first"}]}]},
                {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"second"}]},
                  {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"nested"}]}]}]}]}]},
              {"type":"codeBlock","attrs":{"language":"csharp"},"content":[{"type":"text","text":"var x = 1;"}]},
              {"type":"blockquote","content":[{"type":"paragraph","content":[{"type":"text","text":"quoted"}]}]},
              {"type":"rule"}]}
            """);

        Assert.Contains("# Title", text);
        Assert.Contains("1. first", text);
        Assert.Contains("2. second", text);
        Assert.Contains("  - nested", text);
        Assert.Contains("```csharp\nvar x = 1;\n```", text);
        Assert.Contains("> quoted", text);
        Assert.Contains("---", text);
    }

    [Fact]
    public void Adf_renders_mentions_cards_status_dates_tasks_and_tables()
    {
        var text = Adf("""
            {"type":"doc","content":[
              {"type":"paragraph","content":[
                {"type":"mention","attrs":{"text":"@Alice"}},{"type":"text","text":" see "},
                {"type":"inlineCard","attrs":{"url":"https://example.com/x"}},{"type":"text","text":" "},
                {"type":"status","attrs":{"text":"BLOCKED"}},{"type":"text","text":" "},
                {"type":"date","attrs":{"timestamp":"1790000000000"}}]},
              {"type":"taskList","content":[
                {"type":"taskItem","attrs":{"state":"DONE"},"content":[{"type":"text","text":"shipped"}]},
                {"type":"taskItem","attrs":{"state":"TODO"},"content":[{"type":"text","text":"announce"}]}]},
              {"type":"table","content":[
                {"type":"tableRow","content":[
                  {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"Name"}]}]},
                  {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"Value"}]}]}]},
                {"type":"tableRow","content":[
                  {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]}]},
                  {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"1"}]}]}]}]}]}
            """);

        Assert.Contains("@Alice see https://example.com/x [BLOCKED] 2026-", text);
        Assert.Contains("[x] shipped", text);
        Assert.Contains("[ ] announce", text);
        Assert.Contains("Name | Value", text);
        Assert.Contains("a | 1", text);
    }

    [Fact]
    public void Adf_shows_the_content_of_node_types_it_does_not_know()
    {
        var text = Adf("""
            {"type":"doc","content":[{"type":"fancyNewPanel","content":[{"type":"paragraph","content":[{"type":"text","text":"still readable"}]}]}]}
            """);

        Assert.Equal("still readable", text);
    }

    [Fact]
    public void Adf_copes_with_missing_or_malformed_input()
    {
        Assert.Equal(string.Empty, Atlassian.Mcp.Jira.Adf.ToText(null));
        Assert.Equal(string.Empty, Atlassian.Mcp.Jira.Adf.ToText(JsonNode.Parse("\"just a string\"")));
        Assert.Equal(string.Empty, Atlassian.Mcp.Jira.Adf.ToText(JsonNode.Parse("{}")));
    }

    [Fact]
    public void Adf_output_is_capped()
    {
        var text = Atlassian.Mcp.Jira.Adf.ToText(
            JsonNode.Parse($$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{new string('x', 500)}}"}]}]}"""),
            maxChars: 100);

        Assert.Equal(103, text.Length);
        Assert.EndsWith("...", text);
    }

    [Fact]
    public void Storage_format_keeps_structure_and_names_macros()
    {
        var text = StorageFormat.ToText("""
            <h1>Guide</h1><p>Read <ac:link><ri:page ri:content-title="Setup" /></ac:link> first &amp; then <strong>go</strong>.</p>
            <ac:structured-macro ac:name="code"><ac:plain-text-body><![CDATA[if (a < b) { run(); }]]></ac:plain-text-body></ac:structured-macro>
            <table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
            <ol><li>one</li><li>two</li></ol>
            """);

        Assert.Contains("# Guide", text);
        Assert.Contains("[Setup]", text);
        Assert.Contains("first & then go.", text);
        Assert.Contains("[macro:code]", text);
        Assert.Contains("if (a < b) { run(); }", text);
        Assert.Contains("A | B |", text);
        Assert.Contains("1 | 2 |", text);
        Assert.Contains("- one", text);
        Assert.DoesNotContain("<", text.Replace("a < b", string.Empty));
    }

    [Fact]
    public void Storage_format_handles_empty_input_and_caps_output()
    {
        Assert.Equal(string.Empty, StorageFormat.ToText(null));
        Assert.Equal(string.Empty, StorageFormat.ToText("   "));
        Assert.Equal(53, StorageFormat.ToText("<p>" + new string('x', 500) + "</p>", 50).Length);
    }

    [Theory]
    [InlineData("PROJ-1", "PROJ-1")]
    [InlineData("proj-123", "PROJ-123")]
    [InlineData("AB2_C-99", "AB2_C-99")]
    public void Issue_keys_are_normalised(string input, string expected) =>
        Assert.Equal(expected, Names.IssueKey(input));

    [Theory]
    [InlineData("PROJ")]
    [InlineData("PROJ-")]
    [InlineData("1PROJ-1")]
    [InlineData("PROJ-1 OR 1=1")]
    [InlineData("PROJ-1\nOR 1=1")]
    [InlineData("-1")]
    public void Malformed_issue_keys_are_refused(string input) =>
        Assert.Throws<McpException>(() => Names.IssueKey(input));

    [Theory]
    [InlineData("my-team")]
    [InlineData("Repo_1.x")]
    public void Valid_slugs_pass(string input) => Assert.Equal(input, Names.Slug(input, "slug"));

    [Theory]
    [InlineData("-leading")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("%2e%2e")]
    public void Invalid_slugs_are_refused(string input) =>
        Assert.Throws<McpException>(() => Names.Slug(input, "slug"));

    [Fact]
    public void File_paths_are_escaped_per_segment_and_traversal_is_refused()
    {
        Assert.Equal("docs/read%20me.md", Names.FilePath("/docs/read me.md"));
        Assert.Throws<McpException>(() => Names.FilePath("a/../b"));
        Assert.Throws<McpException>(() => Names.FilePath("a/./b"));
        Assert.Throws<McpException>(() => Names.FilePath("a\0b"));
        Assert.Throws<McpException>(() => Names.FilePath(new string('a', 501)));
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("feature/cache", true)]
    [InlineData("v1.2.3", true)]
    [InlineData("a..b", false)]
    [InlineData("/abs", false)]
    [InlineData("a b", false)]
    [InlineData("a?b=c", false)]
    public void Refs_accept_branch_shapes_only(string input, bool valid)
    {
        if (valid)
        {
            Assert.Equal(input, Names.Ref(input));
        }
        else
        {
            Assert.Throws<McpException>(() => Names.Ref(input));
        }
    }

    [Theory]
    [InlineData("ENG")]
    [InlineData("~jdoe")]
    [InlineData("team.docs")]
    public void Space_keys_accept_personal_and_lowercase_spaces(string input) =>
        Assert.Equal(input, Names.SpaceKey(input));

    [Fact]
    public void Cursor_and_next_link_helpers_round_trip()
    {
        Assert.Equal("a+b==", Shape.CursorFrom("/wiki/api/v2/pages?limit=5&cursor=a%2Bb%3D%3D"));
        Assert.Null(Shape.CursorFrom("/wiki/api/v2/pages?limit=5"));
        Assert.Null(Shape.CursorFrom(null));
        Assert.Equal("tok_1-2", Names.Cursor("tok_1-2"));
        Assert.Throws<McpException>(() => Names.Cursor("https://x/y"));
        Assert.Throws<McpException>(() => Names.Cursor("a b"));
        Assert.Throws<McpException>(() => Names.Cursor("abc\n"));
        Assert.Throws<McpException>(() => Names.Field("summary\n"));
        Assert.Throws<McpException>(() => Names.Number("12\n34", "id"));
    }

    [Fact]
    public void Limit_keeps_the_first_item_even_when_it_is_larger_than_the_cap()
    {
        var items = new JsonNode?[]
        {
            new JsonObject { ["v"] = new string('x', 500) },
            new JsonObject { ["v"] = "second" }
        };

        var (kept, truncated) = Shape.Limit(items, maxItems: 10, maxChars: 100);

        Assert.Single(kept);
        Assert.True(truncated);
    }

    [Fact]
    public void Cache_key_is_the_account_when_known_and_a_token_hash_otherwise()
    {
        Assert.Equal("acct-1", Bearer.CacheKeyFor("acct-1", "tok"));
        Assert.StartsWith("token-", Bearer.CacheKeyFor(null, "tok"));
        Assert.DoesNotContain("tok", Bearer.CacheKeyFor(null, "tok").Replace("token-", string.Empty));
        Assert.NotEqual(Bearer.CacheKeyFor(null, "a"), Bearer.CacheKeyFor(null, "b"));
        Assert.Equal("anonymous", Bearer.CacheKeyFor(null, null));
        Assert.Equal("abc", Bearer.Parse("bearer abc"));
        Assert.Null(Bearer.Parse("Basic abc"));
        Assert.Null(Bearer.Parse("Bearer "));
    }
}
