using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace McpContainerApp.Tools;

/// <summary>
/// Example tools. Replace this class with your own. The [Description] text is the model's only basis
/// for choosing a tool and filling its arguments, so write it for the model, not for a developer.
/// </summary>
[McpServerToolType]
public sealed class ExampleTools(ExampleUpstreamClient upstream)
{
    [McpServerTool(Name = "get_server_info", Title = "Get Server Info",
        ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the name, version, and current UTC time of this MCP server. Use it to confirm the server is reachable.")]
    public static object GetServerInfo() => new
    {
        name = "mcp-container-app",
        version = typeof(ExampleTools).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0",
        utcNow = DateTimeOffset.UtcNow
    };

    [McpServerTool(Name = "search_items", Title = "Search Items",
        ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Searches the upstream system for items matching the text. Returns matching items as JSON. An empty result means nothing matched, not that the call failed.")]
    public async Task<JsonElement> SearchItems(
        [Description("Text to search for.")] string query,
        [Description("Maximum number of items to return, from 1 to 50.")] int top = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ModelContextProtocol.McpException("query is required.");
        }

        return await upstream.SearchAsync(query.Trim(), Math.Clamp(top, 1, 50), cancellationToken);
    }
}
