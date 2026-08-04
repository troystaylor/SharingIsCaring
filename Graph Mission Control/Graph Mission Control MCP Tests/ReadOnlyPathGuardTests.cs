using GraphMissionControl.Mcp.Graph;

namespace GraphMissionControl.Mcp.Tests;

/// <summary>
/// The federated head is read-only by contract, and M365 never enforces that at runtime.
/// Two independent layers hold the line: fetch_work only ever issues GET, and the requested
/// path must match a readOnly entry in the shared index. These cover the second layer.
/// </summary>
public class ReadOnlyPathGuardTests
{
    private static readonly CapabilityIndex Index = CapabilityIndex.LoadEmbedded();

    [Fact]
    public void LoadsOnlyReadOnlyOperations()
    {
        Assert.NotEmpty(Index.ReadOnlyOperations);
        Assert.All(Index.ReadOnlyOperations, op => Assert.True(op.ReadOnly));
    }

    [Theory]
    [InlineData("/me/messages")]
    [InlineData("/me/messages/AAMkAD123")]
    [InlineData("/me/messages?$top=5")]
    [InlineData("/me/calendarView")]
    [InlineData("/me/people")]
    [InlineData("/teams/t1/channels/c1/messages")]
    [InlineData("/sites/contoso.sharepoint.com/lists")]
    public void AllowsReads(string path) => Assert.True(Index.IsAllowedReadPath(path));

    /// <summary>
    /// readOnly cannot be derived from the HTTP verb. These three are POSTs that only read,
    /// and /search/query in particular is the most valuable read in the whole index — a
    /// method-based filter would silently drop it.
    /// </summary>
    [Theory]
    [InlineData("/search/query")]
    [InlineData("/me/findMeetingTimes")]
    [InlineData("/me/calendar/getSchedule")]
    public void AllowsPostOperationsThatOnlyRead(string path) => Assert.True(Index.IsAllowedReadPath(path));

    /// <summary>A placeholder inside a segment still has to match its literal prefix.</summary>
    [Fact]
    public void MatchesLiteralPrefixInsidePlaceholderSegment()
    {
        Assert.True(Index.IsAllowedReadPath("/me/drive/root/search(q='budget')"));
        Assert.False(Index.IsAllowedReadPath("/me/drive/root/delete(q='budget')"));
    }

    [Theory]
    [InlineData("/me/sendMail")]
    [InlineData("/me/messages/abc/send")]
    [InlineData("/me/events/abc/accept")]
    [InlineData("/me/drive/items/x/createLink")]
    [InlineData("/planner/tasks")]
    public void RejectsWrites(string path) => Assert.False(Index.IsAllowedReadPath(path));

    [Theory]
    [InlineData("/servicePrincipals")]
    [InlineData("/applications")]
    [InlineData("/auditLogs/signIns")]
    [InlineData("/me/authentication/methods")]
    public void RejectsPathsOutsideTheApprovedSurface(string path) => Assert.False(Index.IsAllowedReadPath(path));

    [Theory]
    [InlineData("/me/messages/../../users")]
    [InlineData("/./me/messages")]
    [InlineData("")]
    [InlineData("/")]
    public void RejectsTraversalAndJunk(string path) => Assert.False(Index.IsAllowedReadPath(path));

    /// <summary>
    /// A read and a write can share a path, separated only by method. The path guard allows
    /// these; the GET-only rule in fetch_work is what makes the write unreachable. Documented
    /// as a test so nobody "fixes" the guard by rejecting them and breaks reading mail.
    /// </summary>
    [Theory]
    [InlineData("/me/messages")]
    [InlineData("/chats/c1/messages")]
    public void AllowsPathsWhereAReadAndAWriteCoexist(string path) => Assert.True(Index.IsAllowedReadPath(path));
}
