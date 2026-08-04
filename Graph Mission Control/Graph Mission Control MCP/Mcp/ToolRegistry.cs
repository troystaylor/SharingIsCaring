using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace GraphMissionControl.Mcp.Mcp;

/// <summary>
/// The tools this server exposes.
/// </summary>
public sealed class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDescriptor> _tools = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ToolDescriptor> Tools =>
        _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Every tool here reaches Microsoft 365 Copilot through a federated connector, and
    /// federated connectors are read-only by contract. M365 checks <c>readOnlyHint</c> only
    /// at registration time and never enforces it at runtime, so this is the real guard.
    /// </summary>
    public void Add(ToolDescriptor tool)
    {
        if (!tool.Annotations.ReadOnlyHint)
            throw new InvalidOperationException(
                $"tool '{tool.Name}' declares readOnlyHint=false and cannot be exposed: " +
                "federated connectors are read-only by contract");

        if (!_tools.TryAdd(tool.Name, tool))
            throw new InvalidOperationException($"duplicate tool: {tool.Name}");
    }

    public bool TryGet(string name, [NotNullWhen(true)] out ToolDescriptor? tool)
        => _tools.TryGetValue(name, out tool);
}
