using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Well-known resource app IDs for Agent 365 Work IQ MCP servers
    private static readonly Dictionary<string, string> WorkIQServers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Per-server model: each MCP server has its own app ID with Tools.ListInvoke.All scope
        // These IDs are tenant-specific service principals — resolve via servicePrincipals?$filter=displayName eq '...'
        // Common scope for all Work IQ MCP servers:
        // "Tools.ListInvoke.All" (delegated) — invoke tools on the server
        // "McpServersMetadata.Read.All" (delegated) — read MCP server metadata (on Work IQ Tools SP)
    };

    // Well-known resource app IDs
    private const string MicrosoftGraphAppId = "00000003-0000-0000-c000-000000000000";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Pass-through to Graph API — no transformation needed for most operations.
        // The connector routes directly to graph.microsoft.com via host/basePath.
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        return response;
    }
}
