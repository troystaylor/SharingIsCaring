using GraphMissionControl.Mcp.Tools;

namespace GraphMissionControl.Mcp.Tests;

/// <summary>
/// Graph returns `@odata.nextLink` as an absolute URL, so fetch_work has to accept one for paging
/// to work at all. That makes the host check load-bearing: anything other than the Graph v1.0
/// origin must be refused, or the tool becomes an open proxy for whatever host a caller names.
/// </summary>
public class GraphPathNormalizationTests
{
    [Theory]
    [InlineData("/me/messages")]
    [InlineData("/me/events/AAMkAD123")]
    [InlineData("/me/messages?$top=5")]
    public void RelativePathsPassThroughUnchanged(string path)
        => Assert.Equal(path, WorkTools.NormalizeGraphPath(path));

    /// <summary>
    /// Regression: this was implemented with Uri.TryCreate(UriKind.Absolute), which treats a
    /// leading "/" as an absolute file path on Linux but not on Windows. Every relative path was
    /// rejected in the container while these tests passed on the dev machine, so the rule is now
    /// asserted directly rather than inferred from a platform-dependent parse.
    /// </summary>
    [Theory]
    [InlineData("/me/messages")]
    [InlineData("/me/drive/root/children")]
    [InlineData("/teams/t1/channels/c1/messages")]
    [InlineData("me/messages")]
    public void LeadingSlashIsNeverTreatedAsAnAbsoluteUrl(string path)
        => Assert.NotNull(WorkTools.NormalizeGraphPath(path));

    [Fact]
    public void NextLinkIsReducedToARelativePath()
    {
        var nextLink = "https://graph.microsoft.com/v1.0/me/messages?$skiptoken=ABC123";

        Assert.Equal("/me/messages?$skiptoken=ABC123", WorkTools.NormalizeGraphPath(nextLink));
    }

    /// <summary>A relative path may legitimately carry a URL inside a filter value.</summary>
    [Fact]
    public void QueryValueContainingASchemeIsNotTreatedAsAbsolute()
    {
        const string path = "/me/messages?$filter=webLink eq 'https://contoso.example/x'";

        Assert.Equal(path, WorkTools.NormalizeGraphPath(path));
    }

    [Theory]
    [InlineData("https://evil.example/v1.0/me/messages")]                 // unrelated host
    [InlineData("https://graph.microsoft.com.evil.example/v1.0/me")]      // suffix on the host
    [InlineData("https://graph.microsoft.com/v1.0evil.example/me")]       // no separator after v1.0
    [InlineData("https://graph.microsoft.com/beta/me/messages")]          // v1.0 only
    [InlineData("http://graph.microsoft.com/v1.0/me/messages")]           // must be https
    [InlineData("http://169.254.169.254/metadata/instance")]              // instance metadata
    [InlineData("file:///etc/passwd")]
    public void AbsoluteUrlsOutsideGraphV1AreRefused(string url)
        => Assert.Null(WorkTools.NormalizeGraphPath(url));
}
