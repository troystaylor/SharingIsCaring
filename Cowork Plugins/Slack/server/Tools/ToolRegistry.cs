using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SlackCoworkMcp.Tools;

/// <summary>
/// Central registry of MCP tools. Tools are added in <see cref="RegisterAll"/>
/// and looked up by name during <c>tools/call</c>. Routes filter the registry
/// by name using <see cref="FederatedReadOnlyTools"/> (or no filter for full).
/// </summary>
public sealed class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDescriptor> _tools = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ToolDescriptor> Tools => _tools.Values.OrderBy(t => t.Name).ToList();

    public void Add(ToolDescriptor tool)
    {
        if (!_tools.TryAdd(tool.Name, tool))
            throw new InvalidOperationException($"duplicate tool: {tool.Name}");
    }

    public bool TryGet(string name, [NotNullWhen(true)] out ToolDescriptor? tool)
        => _tools.TryGetValue(name, out tool);

    /// <summary>
    /// Tool names exposed to the M365 federated connector. Read-only by spec.
    /// </summary>
    public static readonly IReadOnlySet<string> FederatedReadOnlyTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "search_messages",
        "list_channels",
        "get_channel_history",
        "get_user_info",
        "list_users",
        "scan_slack",
    };

    public void RegisterAll(IServiceProvider _)
    {
        Add(SearchMessagesTool.Build());
        Add(ListChannelsTool.Build());
        Add(GetChannelHistoryTool.Build());
        Add(GetUserInfoTool.Build());
        Add(ListUsersTool.Build());
        Add(SendMessageTool.Build());
        Add(UploadFileTool.Build());
        Add(ScanSlackTool.Build());
        Add(LaunchSlackTool.Build());
        Add(SequenceSlackTool.Build());
    }
}
