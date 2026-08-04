using GraphMissionControl.Mcp.Tools;

namespace GraphMissionControl.Mcp.Tests;

/// <summary>
/// Graph rejects most cross-type searches outright with "Invalid entity type combination".
/// search_work originally defaulted to mail + files + events in one request, so the most
/// likely call an agent could make failed every time. These lock in the documented matrix:
/// https://learn.microsoft.com/graph/api/resources/search-api-overview#known-limitations
/// </summary>
public class SearchGroupingTests
{
    /// <summary>The regression: these three are the tool's defaults and must not share a request.</summary>
    [Fact]
    public void DefaultSourcesSplitIntoSeparateRequests()
    {
        string[] defaults = ["message", "driveItem", "event"];

        var groups = defaults.Select(WorkTools.SearchGroupOf).Distinct().ToArray();

        Assert.Equal(3, groups.Length);
    }

    [Theory]
    [InlineData("message")]
    [InlineData("event")]
    [InlineData("chatMessage")]
    [InlineData("person")]
    public void TypesGraphRequiresAloneNeverShareAGroup(string isolated)
    {
        var others = new[] { "message", "event", "chatMessage", "person", "driveItem" }
            .Where(s => !s.Equals(isolated, StringComparison.OrdinalIgnoreCase));

        var isolatedGroup = WorkTools.SearchGroupOf(isolated);

        Assert.All(others, other => Assert.NotEqual(isolatedGroup, WorkTools.SearchGroupOf(other)));
    }

    /// <summary>The only types Graph permits together.</summary>
    [Fact]
    public void FileTypesShareOneGroup()
    {
        string[] fileTypes = ["drive", "driveItem", "list", "listItem", "site", "externalItem"];

        var groups = fileTypes.Select(WorkTools.SearchGroupOf).Distinct().ToArray();

        Assert.Single(groups);
    }

    /// <summary>Sources arrive from a model, so casing is not guaranteed to match the schema.</summary>
    [Theory]
    [InlineData("chatMessage", "CHATMESSAGE")]
    [InlineData("message", "Message")]
    [InlineData("driveItem", "driveitem")]
    public void GroupingIsCaseInsensitive(string a, string b)
        => Assert.Equal(WorkTools.SearchGroupOf(a), WorkTools.SearchGroupOf(b));
}
