using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  Power Mission Control — MCP Orchestration Template                         ║
// ║                                                                            ║
// ║  Configure your server and mission control options. The framework auto-     ║
// ║  registers scan_{service}, launch_{service}, and sequence_{service}       ║
// ║  tools. You can also add custom tools via handler.AddTool().               ║
// ║                                                                            ║
// ║  Discovery modes:                                                          ║
// ║    Static   — embedded capability index, no external calls                 ║
// ║    Hybrid   — embedded index + live API describe endpoints for schemas     ║
// ║    McpChain — external MCP server for documentation search                 ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Server Configuration ─────────────────────────────────────────────

    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "power-mission-control",
            Version = "1.0.0",
            Title = "Power Mission Control Server",
            Description = "Power Platform custom connector with progressive API discovery via Model Context Protocol"
        },

        // Preferred revision. Legacy clients (Copilot Studio) that open with
        // initialize are detected and served under legacy semantics automatically.
        ProtocolVersion = "2026-07-28",
        SupportedProtocolVersions = new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" },

        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            Prompts = true,
            Completions = true
            // Logging is deprecated as of 2026-07-28; leave off for new servers.
        },

        // Cache hints attached to list and read results (2026-07-28 CacheableResult).
        // The three mission control tools are stable, so the list TTL can be generous.
        ListCacheTtlMs = 900000,      // 15 minutes
        ListCacheScope = "public",
        ResourceCacheTtlMs = 60000,   // 1 minute
        ResourceCacheScope = "private",
        DiscoverCacheTtlMs = 3600000, // 1 hour

        Instructions = "Call scan_{service} first to find the operation you need, then launch_{service} to execute it. " +
            "Use sequence_{service} when several operations must run together."
    };

    // ── Mission Control Configuration ─────────────────────────────────────

    private static readonly MissionControlOptions McOptions = new MissionControlOptions
    {
        // TODO: Set your service name (used in tool names: scan_myservice, launch_myservice)
        ServiceName = "myservice",

        // TODO: Set your API base URL
        BaseApiUrl = "https://api.example.com/v1",

        // Discovery mode: Static (embedded index), Hybrid (index + describe), McpChain (external MCP)
        DiscoveryMode = DiscoveryMode.Static,

        // Batch settings
        BatchMode = BatchMode.Sequential,
        MaxBatchSize = 20,

        // Pagination
        DefaultPageSize = 25,

        // Discovery cache (minutes)
        CacheExpiryMinutes = 10,

        // Describe cache for Hybrid mode (minutes)
        DescribeCacheTTL = 30,

        // Max results returned by discover
        MaxDiscoverResults = 3,

        // Response summarization (set false to disable)
        SummarizeResponses = true,
        MaxBodyLength = 500,
        MaxTextLength = 1000,

        // Default API version (optional, appended to URL if set)
        // DefaultApiVersion = "v1",

        // For Hybrid mode: describe endpoint pattern (use {resource} placeholder)
        // DescribeEndpointPattern = "/sobjects/{resource}/describe",

        // For McpChain mode:
        // McpChainEndpoint = "https://learn.microsoft.com/api/mcp",
        // McpChainToolName = "microsoft_docs_search",
        // McpChainQueryPrefix = "Microsoft Graph",
    };

    // ── Capability Index (Static / Hybrid modes) ─────────────────────────
    //
    //    Embed your capability index as a JSON array. Each entry describes
    //    one API operation the planner can scan for and execute.
    //
    //    Use the companion generate-capability-index.prompt.md to create
    //    this from API docs, Swagger files, or Postman collections.
    //

    private const string CAPABILITY_INDEX = @"[
        {
            ""cid"": ""list_items"",
            ""endpoint"": ""/items"",
            ""method"": ""GET"",
            ""outcome"": ""List all items with optional filtering and pagination"",
            ""domain"": ""data"",
            ""requiredParams"": [],
            ""optionalParams"": [""status"", ""page"", ""per_page""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""status\"":{\""type\"":\""string\"",\""enum\"":[\""active\"",\""archived\""]},\""page\"":{\""type\"":\""integer\""},\""per_page\"":{\""type\"":\""integer\"",\""default\"":25}}}""
        },
        {
            ""cid"": ""get_item"",
            ""endpoint"": ""/items/{id}"",
            ""method"": ""GET"",
            ""outcome"": ""Get a single item by its unique identifier"",
            ""domain"": ""data"",
            ""requiredParams"": [""id""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""id\"":{\""type\"":\""string\"",\""description\"":\""Item ID\""}},\""required\"":[\""id\""]}""
        },
        {
            ""cid"": ""create_item"",
            ""endpoint"": ""/items"",
            ""method"": ""POST"",
            ""outcome"": ""Create a new item record"",
            ""domain"": ""data"",
            ""requiredParams"": [""name""],
            ""optionalParams"": [""description"", ""status""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\""},\""description\"":{\""type\"":\""string\""},\""status\"":{\""type\"":\""string\""}},\""required\"":[\""name\""]}""
        }
    ]";

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        // 1. Create the handler
        var handler = new McpRequestHandler(Options);

        // 2. Register mission control tools (scan, launch, sequence)
        MissionControl.RegisterMission(handler, McOptions, CAPABILITY_INDEX, this);

        // 3. Register any additional custom tools
        RegisterCustomTools(handler);

        // 4. Wire up logging (optional)
        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        // 5. Handle the request
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(
            body,
            McpTransportHeaders.FromRequest(this.Context.Request),
            this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        // 6. Return the response
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Custom Tools (Optional) ──────────────────────────────────────────
    //
    //    Add any connector-specific tools beyond scan/launch/sequence here.
    //    These appear alongside the mission control tools in tools/list.
    //

    private void RegisterCustomTools(McpRequestHandler handler)
    {
        // TODO: Add custom tools here if needed
        //
        // handler.AddTool("describe_object", "Get live field-level schema for a specific resource.",
        //     schema: s => s.String("object", "The API resource name", required: true),
        //     handler: async (args, ct) =>
        //     {
        //         var obj = args.Value<string>("object");
        //         return await SendExternalRequestAsync(HttpMethod.Get, $"{McOptions.BaseApiUrl}/describe/{obj}");
        //     });
    }

    // ── Smart Defaults (Optional) ────────────────────────────────────────
    //
    //    Uncomment and customize to add domain-specific parameter defaults.
    //    These run before every invoke call and can inject query parameters.
    //
    //    Example - auto-add date range for calendar endpoints:
    //
    //    static Script()
    //    {
    //        McOptions.SmartDefaults = new Dictionary<string, Action<string, JObject>>
    //        {
    //            ["/calendar"] = (endpoint, queryParams) =>
    //            {
    //                if (queryParams["startDate"] == null)
    //                    queryParams["startDate"] = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
    //                if (queryParams["endDate"] == null)
    //                    queryParams["endDate"] = DateTime.UtcNow.Date.AddDays(7).ToString("yyyy-MM-dd");
    //            }
    //        };
    //    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Send a request to an external API.
    /// Forwards the connector's Authorization header and handles response parsing.
    /// </summary>
    private async Task<JObject> SendExternalRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"API request failed ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    /// <summary>Get a required string argument; throws ArgumentException if missing.</summary>
    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    /// <summary>Get an optional string argument with a default fallback.</summary>
    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    /// <summary>Read a connection parameter by name (null-safe).</summary>
    private string GetConnectionParameter(string name)
    {
        try
        {
            var raw = this.Context.ConnectionParameters[name]?.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch { return null; }
    }

    // ── Application Insights (Optional) ──────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
            };

            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: MCP FRAMEWORK + ORCHESTRATION ENGINE                           ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler (MCP 2026-07-28, dual-era) plus the mission     ║
// ║  control classes: MissionControlOptions, CapabilityIndex, ApiProxy,        ║
// ║  McpChainClient, DiscoveryEngine, MissionControl.                          ║
// ║                                                                            ║
// ║  2026-07-28 removed the initialize handshake and made MCP stateless: every ║
// ║  request carries its own protocol version, identity, and capabilities in   ║
// ║  _meta. Power Platform connectors are stateless by construction, so the    ║
// ║  runtime and the protocol now agree rather than fight.                     ║
// ║                                                                            ║
// ║  The handler serves BOTH eras from one endpoint. Era is chosen per         ║
// ║  request; Copilot Studio is a legacy client and is answered exactly as it  ║
// ║  was before this revision.                                                 ║
// ║                                                                            ║
// ║  McpChainClient is a dual-era MCP CLIENT: this connector also calls out    ║
// ║  to an external MCP server in McpChain discovery mode.                     ║
// ║                                                                            ║
// ║  Do not modify unless extending the framework itself.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Protocol Constants ───────────────────────────────────────────────────────

/// <summary>Reserved _meta keys, extension identifiers, and URI schemes defined by MCP.</summary>
public static class McpKeys
{
    public const string ProtocolVersion = "io.modelcontextprotocol/protocolVersion";
    public const string ClientInfo = "io.modelcontextprotocol/clientInfo";
    public const string ClientCapabilities = "io.modelcontextprotocol/clientCapabilities";
    public const string ServerInfo = "io.modelcontextprotocol/serverInfo";
    public const string LogLevel = "io.modelcontextprotocol/logLevel";
    public const string SubscriptionId = "io.modelcontextprotocol/subscriptionId";

    // Official extensions, advertised through capabilities.extensions.
    public const string ExtensionTasks = "io.modelcontextprotocol/tasks";
    public const string ExtensionUi = "io.modelcontextprotocol/ui";

    // Agent Skills over MCP.
    public const string SkillScheme = "skill://";
    public const string SkillIndexUri = "skill://index.json";
}

// ── Configuration Types ──────────────────────────────────────────────────────

/// <summary>Server identity reported by server/discover and in each result's _meta.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }

    /// <summary>Optional icons array, per the icons field in the MCP base spec.</summary>
    public JArray Icons { get; set; }
}

/// <summary>Capabilities advertised by server/discover and initialize.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Completions { get; set; }

    /// <summary>Deprecated in 2026-07-28. Advertised to legacy clients only.</summary>
    public bool Logging { get; set; }

    /// <summary>
    /// Optional extension map keyed by extension identifier, for example
    /// { "io.modelcontextprotocol/ui": { "mimeTypes": [ "text/html;profile=mcp-app" ] } }.
    /// </summary>
    public JObject Extensions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();

    /// <summary>Preferred revision. Listed first by server/discover.</summary>
    public string ProtocolVersion { get; set; } = "2026-07-28";

    /// <summary>Every revision this server accepts. Must contain ProtocolVersion.</summary>
    public List<string> SupportedProtocolVersions { get; set; } =
        new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" };

    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();

    /// <summary>Natural-language guidance for the model, returned by server/discover.</summary>
    public string Instructions { get; set; }

    // ── Cache hints (2026-07-28 CacheableResult) ──────────────────────────

    /// <summary>Freshness hint in milliseconds for tools/prompts/resources list results.</summary>
    public int ListCacheTtlMs { get; set; } = 300000;

    /// <summary>"public" when list results are identical for every caller, otherwise "private".</summary>
    public string ListCacheScope { get; set; } = "public";

    public int ResourceCacheTtlMs { get; set; } = 60000;

    /// <summary>"private" by default: resource content commonly varies by authenticated user.</summary>
    public string ResourceCacheScope { get; set; } = "private";

    public int DiscoverCacheTtlMs { get; set; } = 3600000;

    /// <summary>
    /// Compare the Mcp-Method and Mcp-Name headers against the request body and reject a
    /// mismatch with HeaderMismatch. Absent headers are ignored, because the Power Platform
    /// gateway makes no guarantee that it forwards them.
    /// </summary>
    public bool ValidateRequestHeaders { get; set; } = true;
}

// ── Transport Headers ────────────────────────────────────────────────────────

/// <summary>
/// The Streamable HTTP headers the framework cares about. Captured in the connector
/// entry point and handed to HandleAsync so the framework never touches ScriptBase.
/// </summary>
public class McpTransportHeaders
{
    public string ProtocolVersion { get; set; }
    public string Method { get; set; }
    public string Name { get; set; }

    public static McpTransportHeaders FromRequest(HttpRequestMessage request)
    {
        var headers = new McpTransportHeaders();
        if (request == null) return headers;

        headers.ProtocolVersion = FirstOrNull(request, "MCP-Protocol-Version");
        headers.Method = FirstOrNull(request, "Mcp-Method");
        headers.Name = FirstOrNull(request, "Mcp-Name");
        return headers;
    }

    private static string FirstOrNull(HttpRequestMessage request, string headerName)
    {
        IEnumerable<string> values;
        if (!request.Headers.TryGetValues(headerName, out values)) return null;
        foreach (var value in values) return value;
        return null;
    }
}

// ── Request Context ──────────────────────────────────────────────────────────

/// <summary>
/// Everything the framework knows about one request. This replaces the connection
/// state that 2026-07-28 removed: era, negotiated version, client identity, and the
/// answers a client attaches when it retries a multi round-trip request.
/// </summary>
public class McpRequestContext
{
    public JToken Id { get; set; }
    public string Method { get; set; }

    /// <summary>True when the caller speaks 2026-07-28 or later.</summary>
    public bool IsModern { get; set; }

    public string ProtocolVersion { get; set; }
    public JObject ClientInfo { get; set; }
    public JObject ClientCapabilities { get; set; }
    public string LogLevel { get; set; }

    /// <summary>Answers supplied on an MRTR retry, keyed by input request name.</summary>
    public JObject InputResponses { get; set; }

    /// <summary>Opaque state handed back with a previous input_required result.</summary>
    public string RequestState { get; set; }

    /// <summary>True when the client declared the given extension identifier.</summary>
    public bool SupportsExtension(string extensionId)
    {
        var extensions = ClientCapabilities?["extensions"] as JObject;
        return extensions != null && extensions[extensionId] != null;
    }
}

// ── Agent Skills ─────────────────────────────────────────────────────────────

/// <summary>One resource file bundled with a skill.</summary>
internal class McpSkillResource
{
    public string Path { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public Func<string> Content { get; set; }
}

/// <summary>Fluent collector for a skill's sibling resource files.</summary>
public class McpSkillResourceBuilder
{
    internal readonly List<McpSkillResource> Resources = new List<McpSkillResource>();

    /// <summary>
    /// Add a file served alongside SKILL.md. Use the relative path the skill body
    /// references, for example "references/policy.md" or "assets/template.md".
    /// </summary>
    public McpSkillResourceBuilder Resource(string path, string description, Func<string> content, string mimeType = "text/markdown")
    {
        Resources.Add(new McpSkillResource
        {
            Path = path,
            Description = description,
            MimeType = mimeType,
            Content = content
        });
        return this;
    }
}

internal class McpSkillDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Instructions { get; set; }
    public string License { get; set; }
    public string Compatibility { get; set; }
    public JObject Metadata { get; set; }
    public List<McpSkillResource> Resources { get; set; } = new List<McpSkillResource>();

    public string SkillUri { get { return McpKeys.SkillScheme + Name + "/SKILL.md"; } }

    public string ResourceUri(string path) { return McpKeys.SkillScheme + Name + "/" + path; }

    /// <summary>Render SKILL.md: YAML frontmatter followed by the markdown body.</summary>
    public string RenderSkillMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(Name).Append('\n');
        sb.Append("description: ").Append(EscapeYaml(Description)).Append('\n');

        if (!string.IsNullOrWhiteSpace(License))
            sb.Append("license: ").Append(EscapeYaml(License)).Append('\n');

        if (!string.IsNullOrWhiteSpace(Compatibility))
            sb.Append("compatibility: ").Append(EscapeYaml(Compatibility)).Append('\n');

        if (Metadata != null && Metadata.Count > 0)
        {
            sb.Append("metadata:\n");
            foreach (var prop in Metadata.Properties())
                sb.Append("  ").Append(prop.Name).Append(": ").Append(EscapeYaml(prop.Value?.ToString())).Append('\n');
        }

        sb.Append("---\n\n");
        sb.Append(Instructions ?? string.Empty);
        return sb.ToString();
    }

    /// <summary>Quote any scalar that would otherwise break the frontmatter block.</summary>
    private static string EscapeYaml(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var flattened = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return "\"" + flattened.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

// ── Mission Control Configuration ────────────────────────────────────────────

public enum DiscoveryMode { Static, Hybrid, McpChain }
public enum BatchMode { Sequential, BatchEndpoint }

public class MissionControlOptions
{
    /// <summary>Service name used in tool names (scan_{ServiceName}, launch_{ServiceName}).</summary>
    public string ServiceName { get; set; } = "api";

    /// <summary>Discovery mode: Static (embedded index), Hybrid (index + describe), McpChain (external MCP).</summary>
    public DiscoveryMode DiscoveryMode { get; set; } = DiscoveryMode.Static;

    /// <summary>Base URL for all API calls (no trailing slash).</summary>
    public string BaseApiUrl { get; set; }

    /// <summary>Default API version appended to URL if set (e.g., "v1.0").</summary>
    public string DefaultApiVersion { get; set; }

    /// <summary>Batch execution mode: Sequential (one at a time) or BatchEndpoint (native $batch).</summary>
    public BatchMode BatchMode { get; set; } = BatchMode.Sequential;

    /// <summary>Path for native batch endpoint (e.g., "/$batch").</summary>
    public string BatchEndpointPath { get; set; } = "/$batch";

    /// <summary>Maximum requests per batch call.</summary>
    public int MaxBatchSize { get; set; } = 20;

    /// <summary>Default page size for GET collection endpoints ($top).</summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>Discovery cache TTL in minutes.</summary>
    public int CacheExpiryMinutes { get; set; } = 10;

    /// <summary>Describe/metadata cache TTL in minutes (Hybrid mode).</summary>
    public int DescribeCacheTTL { get; set; } = 30;

    /// <summary>Maximum results returned by discover tool.</summary>
    public int MaxDiscoverResults { get; set; } = 3;

    /// <summary>Enable response summarization (HTML stripping, truncation). Default true.</summary>
    public bool SummarizeResponses { get; set; } = true;

    /// <summary>Max characters for body/HTML fields in summarization.</summary>
    public int MaxBodyLength { get; set; } = 500;

    /// <summary>Max characters for text fields in summarization.</summary>
    public int MaxTextLength { get; set; } = 1000;

    // Hybrid mode
    /// <summary>Describe endpoint pattern with {resource} placeholder (e.g., "/sobjects/{resource}/describe").</summary>
    public string DescribeEndpointPattern { get; set; }

    // McpChain mode
    /// <summary>External MCP server endpoint URL.</summary>
    public string McpChainEndpoint { get; set; }

    /// <summary>Tool name to call on the external MCP server.</summary>
    public string McpChainToolName { get; set; }

    /// <summary>Query prefix for MCP chain searches (e.g., "Microsoft Graph").</summary>
    public string McpChainQueryPrefix { get; set; }

    /// <summary>Author-defined smart defaults. Key = endpoint pattern substring, Value = action(endpoint, queryParams).</summary>
    public Dictionary<string, Action<string, JObject>> SmartDefaults { get; set; }
}

// ── Capability Entry ─────────────────────────────────────────────────────────

public class CapabilityEntry
{
    public string Cid { get; set; }
    public string Endpoint { get; set; }
    public string Method { get; set; }
    public string Outcome { get; set; }
    public string Domain { get; set; }
    public string[] RequiredParams { get; set; }
    public string[] OptionalParams { get; set; }
    public string SchemaJson { get; set; }
}

// ── Capability Index ─────────────────────────────────────────────────────────

public class CapabilityIndex
{
    private readonly List<CapabilityEntry> _entries;

    public CapabilityIndex(string indexJson)
    {
        _entries = new List<CapabilityEntry>();
        if (string.IsNullOrWhiteSpace(indexJson)) return;

        var array = JArray.Parse(indexJson);
        foreach (var item in array)
        {
            _entries.Add(new CapabilityEntry
            {
                Cid = item.Value<string>("cid") ?? "",
                Endpoint = item.Value<string>("endpoint") ?? "",
                Method = (item.Value<string>("method") ?? "GET").ToUpperInvariant(),
                Outcome = item.Value<string>("outcome") ?? "",
                Domain = item.Value<string>("domain") ?? "",
                RequiredParams = item["requiredParams"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                OptionalParams = item["optionalParams"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                SchemaJson = item.Value<string>("schemaJson")
            });
        }
    }

    /// <summary>
    /// Search the index by keyword query and optional domain filter.
    /// Scoring: exact cid match (100), domain match (50), keyword overlap on outcome (10 per word).
    /// Returns top N results ordered by score descending.
    /// </summary>
    public List<CapabilityEntry> Search(string query, string domain = null, int maxResults = 3)
    {
        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(domain))
            return _entries.Take(maxResults).ToList();

        var queryLower = (query ?? "").ToLowerInvariant();
        var queryWords = queryLower.Split(new[] { ' ', '_', '-', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var domainLower = (domain ?? "").ToLowerInvariant();

        var scored = new List<KeyValuePair<CapabilityEntry, int>>();

        foreach (var entry in _entries)
        {
            int score = 0;
            var cidLower = entry.Cid.ToLowerInvariant();
            var outcomeLower = entry.Outcome.ToLowerInvariant();
            var entryDomainLower = entry.Domain.ToLowerInvariant();
            var endpointLower = entry.Endpoint.ToLowerInvariant();

            // Exact CID match
            if (cidLower == queryLower) score += 100;
            // Partial CID match
            else if (cidLower.Contains(queryLower) || queryLower.Contains(cidLower)) score += 60;

            // Domain filter match
            if (!string.IsNullOrWhiteSpace(domainLower) && entryDomainLower == domainLower) score += 50;
            else if (!string.IsNullOrWhiteSpace(domainLower) && entryDomainLower != domainLower) score -= 20;

            // Keyword overlap on outcome, endpoint, and cid
            foreach (var word in queryWords)
            {
                if (word.Length < 2) continue;
                if (outcomeLower.Contains(word)) score += 10;
                if (cidLower.Contains(word)) score += 15;
                if (endpointLower.Contains(word)) score += 8;
            }

            // Method match (if query contains GET, POST, etc.)
            var methodUpper = entry.Method.ToUpperInvariant();
            if (queryLower.Contains(methodUpper.ToLowerInvariant()) && methodUpper.Length > 2) score += 5;

            if (score > 0) scored.Add(new KeyValuePair<CapabilityEntry, int>(entry, score));
        }

        return scored
            .OrderByDescending(kv => kv.Value)
            .Take(maxResults)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>Get a specific capability by CID (exact match).</summary>
    public CapabilityEntry Get(string cid)
    {
        return _entries.FirstOrDefault(e =>
            string.Equals(e.Cid, cid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get all entries in the index.</summary>
    public List<CapabilityEntry> GetAll() => new List<CapabilityEntry>(_entries);

    /// <summary>Total number of indexed capabilities.</summary>
    public int Count => _entries.Count;
}

// ── Cache Infrastructure ─────────────────────────────────────────────────────

internal class CacheEntry
{
    public JObject Result { get; set; }
    public DateTime Expiry { get; set; }
}

// ── API Proxy ────────────────────────────────────────────────────────────────

public class ApiProxy
{
    private readonly MissionControlOptions _options;
    private const int MAX_RETRIES = 3;

    // Common collection endpoint patterns for auto-$top injection
    private static readonly string[] CollectionPatterns = new[]
    {
        "/messages", "/events", "/users", "/groups", "/teams", "/channels",
        "/members", "/children", "/items", "/lists", "/tasks", "/contacts",
        "/calendars", "/drives", "/sites", "/records", "/customers", "/orders",
        "/products", "/invoices", "/accounts", "/leads", "/cases", "/tickets"
    };

    public ApiProxy(MissionControlOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Execute a single API request. Forwards auth, applies smart defaults,
    /// handles retries, and summarizes responses.
    /// </summary>
    public async Task<JObject> InvokeAsync(
        ScriptBase context,
        string endpoint,
        string method,
        JObject body = null,
        JObject queryParams = null,
        string apiVersion = null,
        CapabilityIndex index = null)
    {
        // Validate endpoint (warn but allow unknown)
        string warning = null;
        if (index != null)
        {
            var match = index.GetAll().Any(e =>
                string.Equals(e.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase) ||
                EndpointMatchesPattern(e.Endpoint, endpoint));
            if (!match)
                warning = $"Endpoint '{endpoint}' is not in the capability index. Proceeding anyway.";
        }

        // Build URL
        var url = BuildUrl(endpoint, method, queryParams, apiVersion);

        // Execute with retry
        return await ExecuteWithRetryAsync(context, url, method, body, warning).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute multiple API requests. Sequential mode: one at a time, in order.
    /// BatchEndpoint mode: single POST to $batch path.
    /// </summary>
    public async Task<JObject> BatchInvokeAsync(
        ScriptBase context,
        JArray requests,
        string apiVersion = null,
        CapabilityIndex index = null)
    {
        if (requests == null || requests.Count == 0)
            return CreateErrorResult("batch_empty", "No requests provided",
                "Provide at least one request in the requests array.",
                "The requests array is empty.");

        if (requests.Count > _options.MaxBatchSize)
            return CreateErrorResult("batch_too_large",
                $"Batch exceeds maximum size of {_options.MaxBatchSize}",
                $"Split into batches of {_options.MaxBatchSize} or fewer.",
                $"Too many requests. Maximum is {_options.MaxBatchSize}, got {requests.Count}.");

        if (_options.BatchMode == BatchMode.BatchEndpoint)
            return await ExecuteBatchEndpointAsync(context, requests, apiVersion).ConfigureAwait(false);

        return await ExecuteSequentialBatchAsync(context, requests, apiVersion, index).ConfigureAwait(false);
    }

    /// <summary>Strip HTML tags, decode entities, collapse whitespace, truncate.</summary>
    public void SummarizeResponse(JToken token)
    {
        if (!_options.SummarizeResponses) return;
        SummarizeToken(token, _options.MaxBodyLength, _options.MaxTextLength);
    }

    // ── Private: URL Building ────────────────────────────────────────────

    private string BuildUrl(string endpoint, string method, JObject queryParams, string apiVersion)
    {
        var baseUrl = _options.BaseApiUrl?.TrimEnd('/') ?? "";
        var version = apiVersion ?? _options.DefaultApiVersion;

        // Build path
        var path = endpoint?.TrimStart('/') ?? "";
        string url;
        if (!string.IsNullOrWhiteSpace(version) && !baseUrl.Contains(version))
            url = $"{baseUrl}/{version}/{path}";
        else
            url = $"{baseUrl}/{path}";

        // Apply smart defaults
        queryParams = queryParams ?? new JObject();
        ApplySmartDefaults(endpoint, method, queryParams);

        // Build query string
        var queryParts = new List<string>();
        foreach (var prop in queryParams.Properties())
        {
            var val = prop.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
                queryParts.Add($"{Uri.EscapeDataString(prop.Name)}={Uri.EscapeDataString(val)}");
        }

        if (queryParts.Count > 0)
            url += (url.Contains("?") ? "&" : "?") + string.Join("&", queryParts);

        return url;
    }

    private void ApplySmartDefaults(string endpoint, string method, JObject queryParams)
    {
        var endpointLower = (endpoint ?? "").ToLowerInvariant();
        var methodUpper = (method ?? "GET").ToUpperInvariant();

        // Built-in: $top for GET collection endpoints
        if (methodUpper == "GET" && IsCollectionEndpoint(endpointLower))
        {
            if (queryParams["$top"] == null && queryParams["top"] == null &&
                queryParams["per_page"] == null && queryParams["limit"] == null &&
                queryParams["pageSize"] == null)
            {
                queryParams["$top"] = _options.DefaultPageSize;
            }
        }

        // Author-defined smart defaults
        if (_options.SmartDefaults != null)
        {
            foreach (var kvp in _options.SmartDefaults)
            {
                if (endpointLower.Contains(kvp.Key.ToLowerInvariant()))
                    kvp.Value?.Invoke(endpoint, queryParams);
            }
        }
    }

    private bool IsCollectionEndpoint(string endpointLower)
    {
        // Ends with a collection name (not an ID segment)
        var lastSegment = endpointLower.Split('/').LastOrDefault() ?? "";
        if (lastSegment.StartsWith("{") || Guid.TryParse(lastSegment, out _)) return false;

        return CollectionPatterns.Any(p => endpointLower.EndsWith(p, StringComparison.OrdinalIgnoreCase))
            || (lastSegment.Length > 2 && !lastSegment.Contains("{"));
    }

    private static bool EndpointMatchesPattern(string pattern, string endpoint)
    {
        var patternParts = pattern.Split('/');
        var endpointParts = endpoint.Split('/');
        if (patternParts.Length != endpointParts.Length) return false;

        for (int i = 0; i < patternParts.Length; i++)
        {
            if (patternParts[i].StartsWith("{") && patternParts[i].EndsWith("}")) continue;
            if (!string.Equals(patternParts[i], endpointParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    // ── Private: HTTP Execution ──────────────────────────────────────────

    private async Task<JObject> ExecuteWithRetryAsync(
        ScriptBase context, string url, string method,
        JObject body, string warning, int retryCount = 0)
    {
        var httpMethod = new HttpMethod(method.ToUpperInvariant());
        var request = new HttpRequestMessage(httpMethod, url);

        // Forward auth
        if (context.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = context.Context.Request.Headers.Authorization;

        // Content-Type for write operations
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (httpMethod == HttpMethod.Post || httpMethod.Method == "PATCH" || httpMethod.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await context.Context.SendAsync(request, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return CreateErrorResult("connection_error", ex.Message,
                "Check that the API is reachable and try again.",
                $"Failed to connect to {url}: {ex.Message}");
        }

        var statusCode = (int)response.StatusCode;
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // 429: Retry with backoff
        if (statusCode == 429 && retryCount < MAX_RETRIES)
        {
            var retryAfter = 5;
            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var val = retryValues.FirstOrDefault();
                if (int.TryParse(val, out var seconds))
                    retryAfter = Math.Min(seconds, 30);
            }
            await Task.Delay(retryAfter * 1000).ConfigureAwait(false);
            return await ExecuteWithRetryAsync(context, url, method, body, warning, retryCount + 1).ConfigureAwait(false);
        }

        // Error responses
        if (statusCode == 401 || statusCode == 403)
        {
            return CreateErrorResult("permission_denied", $"HTTP {statusCode}",
                "Check that your account has the required permissions. Contact your administrator if needed.",
                $"Access denied ({statusCode}). You don't have permission for this operation.");
        }

        if (statusCode == 404)
        {
            return CreateErrorResult("not_found", $"HTTP 404",
                "Verify the endpoint path and any resource IDs. Use scan to find the correct endpoint.",
                $"Resource not found at {url}. The endpoint may be incorrect or the resource may not exist.");
        }

        if (statusCode >= 400)
        {
            var errorDetail = "";
            try { errorDetail = JObject.Parse(responseBody)?["message"]?.ToString() ?? responseBody; }
            catch { errorDetail = responseBody; }

            return CreateErrorResult("api_error", $"HTTP {statusCode}: {errorDetail}",
                "Check the request parameters and try again.",
                $"API returned error {statusCode}: {errorDetail}");
        }

        // Success
        JToken data;
        try
        {
            data = string.IsNullOrWhiteSpace(responseBody)
                ? new JObject { ["success"] = true }
                : JToken.Parse(responseBody);
        }
        catch
        {
            data = new JObject { ["text"] = responseBody };
        }

        // Summarize if enabled
        SummarizeResponse(data);

        // Build result
        var result = new JObject
        {
            ["success"] = true,
            ["data"] = data
        };

        // Check for pagination
        if (data is JObject dataObj)
        {
            var nextLink = dataObj.Value<string>("@odata.nextLink")
                ?? dataObj.Value<string>("nextLink")
                ?? dataObj.Value<string>("next_page_url")
                ?? dataObj.Value<string>("next");

            if (!string.IsNullOrWhiteSpace(nextLink))
            {
                result["hasMore"] = true;
                result["nextLink"] = nextLink;
                result["nextPageHint"] = $"Call launch again with the nextLink value as the full URL to get the next page.";
            }
        }

        if (!string.IsNullOrWhiteSpace(warning))
            result["warning"] = warning;

        return result;
    }

    // ── Private: Batch Execution ─────────────────────────────────────────

    private async Task<JObject> ExecuteSequentialBatchAsync(
        ScriptBase context, JArray requests, string apiVersion, CapabilityIndex index)
    {
        var responses = new JArray();
        int successCount = 0, errorCount = 0;

        foreach (var req in requests)
        {
            var id = req.Value<string>("id") ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            var endpoint = req.Value<string>("endpoint") ?? "";
            var method = req.Value<string>("method") ?? "GET";
            var body = req["body"] as JObject;
            var qp = req["query_params"] as JObject;

            try
            {
                var result = await InvokeAsync(context, endpoint, method, body, qp, apiVersion, index).ConfigureAwait(false);
                var success = result.Value<bool?>("success") ?? false;
                if (success) successCount++; else errorCount++;

                responses.Add(new JObject
                {
                    ["id"] = id,
                    ["success"] = success,
                    ["status"] = success ? 200 : 400,
                    ["data"] = result
                });
            }
            catch (Exception ex)
            {
                errorCount++;
                responses.Add(new JObject
                {
                    ["id"] = id,
                    ["success"] = false,
                    ["status"] = 500,
                    ["error"] = ex.Message
                });
            }
        }

        return new JObject
        {
            ["success"] = errorCount == 0,
            ["batchSize"] = requests.Count,
            ["successCount"] = successCount,
            ["errorCount"] = errorCount,
            ["responses"] = responses
        };
    }

    private async Task<JObject> ExecuteBatchEndpointAsync(
        ScriptBase context, JArray requests, string apiVersion)
    {
        var baseUrl = _options.BaseApiUrl?.TrimEnd('/') ?? "";
        var version = apiVersion ?? _options.DefaultApiVersion;
        var batchUrl = !string.IsNullOrWhiteSpace(version)
            ? $"{baseUrl}/{version}{_options.BatchEndpointPath}"
            : $"{baseUrl}{_options.BatchEndpointPath}";

        var batchRequests = new JArray();
        foreach (var req in requests)
        {
            var batchReq = new JObject
            {
                ["id"] = req.Value<string>("id") ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                ["method"] = (req.Value<string>("method") ?? "GET").ToUpperInvariant(),
                ["url"] = req.Value<string>("endpoint") ?? ""
            };

            var body = req["body"] as JObject;
            var method = batchReq.Value<string>("method");
            if (body != null && (method == "POST" || method == "PATCH" || method == "PUT"))
            {
                batchReq["body"] = body;
                batchReq["headers"] = new JObject { ["Content-Type"] = "application/json" };
            }

            batchRequests.Add(batchReq);
        }

        var batchBody = new JObject { ["requests"] = batchRequests };
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, batchUrl)
        {
            Content = new StringContent(batchBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };

        if (context.Context.Request.Headers.Authorization != null)
            httpRequest.Headers.Authorization = context.Context.Request.Headers.Authorization;

        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await context.Context.SendAsync(httpRequest, context.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject batchResponse;
        try { batchResponse = JObject.Parse(content); }
        catch { return CreateErrorResult("batch_parse_error", "Failed to parse batch response", "Try individual requests instead.", content); }

        var batchResponses = batchResponse["responses"] as JArray ?? new JArray();
        var resultResponses = new JArray();
        int successCount = 0, errorCount = 0;

        foreach (var resp in batchResponses)
        {
            var status = resp.Value<int?>("status") ?? 0;
            var success = status >= 200 && status < 300;
            if (success) successCount++; else errorCount++;

            var processed = new JObject
            {
                ["id"] = resp.Value<string>("id"),
                ["status"] = status,
                ["success"] = success
            };

            if (success)
            {
                var bodyData = resp["body"];
                if (bodyData != null) SummarizeResponse(bodyData);
                processed["data"] = bodyData;
            }
            else
            {
                processed["error"] = resp["body"];
            }

            resultResponses.Add(processed);
        }

        return new JObject
        {
            ["success"] = errorCount == 0,
            ["batchSize"] = requests.Count,
            ["successCount"] = successCount,
            ["errorCount"] = errorCount,
            ["responses"] = resultResponses
        };
    }

    // ── Private: Response Summarization ──────────────────────────────────

    private void SummarizeToken(JToken token, int maxBodyLength, int maxTextLength)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties().ToList())
            {
                var name = prop.Name.ToLowerInvariant();
                if ((name == "body" || name == "bodypreview" || name == "description") && prop.Value.Type == JTokenType.String)
                {
                    var val = prop.Value.ToString();
                    var stripped = StripHtml(val);
                    if (stripped.Length > maxBodyLength)
                    {
                        obj[prop.Name] = stripped.Substring(0, maxBodyLength) + "...";
                        obj[prop.Name + "_truncated"] = true;
                    }
                    else
                    {
                        obj[prop.Name] = stripped;
                    }
                }
                else if (prop.Value.Type == JTokenType.String)
                {
                    var val = prop.Value.ToString();
                    if (val.Length > maxTextLength)
                    {
                        obj[prop.Name] = val.Substring(0, maxTextLength) + "...";
                        obj[prop.Name + "_truncated"] = true;
                    }
                }
                else if (prop.Value is JObject || prop.Value is JArray)
                {
                    SummarizeToken(prop.Value, maxBodyLength, maxTextLength);
                }
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr)
                SummarizeToken(item, maxBodyLength, maxTextLength);
        }
    }

    /// <summary>Strip HTML tags, decode common entities, collapse whitespace.</summary>
    public static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? "";
        html = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = html.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<")
                    .Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#39;", "'");
        html = Regex.Replace(html, @"\s+", " ").Trim();
        return html;
    }

    // ── Private: Error Helpers ───────────────────────────────────────────

    private static JObject CreateErrorResult(string error, string message, string suggestion, string friendlyMessage)
    {
        return new JObject
        {
            ["success"] = false,
            ["error"] = error,
            ["code"] = error,
            ["message"] = message,
            ["friendlyMessage"] = friendlyMessage,
            ["suggestion"] = suggestion
        };
    }
}

// ── MCP Chain Client ─────────────────────────────────────────────────────────

/// <summary>One upstream MCP response, with enough detail to detect the server's era.</summary>
public class McpUpstreamResponse
{
    public int StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public JObject Body { get; set; }
}

public class McpChainClient
{
    private static readonly Dictionary<string, CacheEntry> _chainCache =
        new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

    // Era is a property of the endpoint, not the request, so the probe runs once.
    private static readonly Dictionary<string, bool> _endpointIsLegacy =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Discover capabilities by chaining to an external MCP server.
    ///
    /// This is a DUAL-ERA CLIENT. 2026-07-28 removed the initialize handshake, so a
    /// modern upstream would reject one. It tries the modern path first — a single
    /// tools/call carrying per-request _meta — and falls back to the legacy
    /// initialize handshake only when the response looks like a legacy server.
    /// The era is remembered per endpoint so the probe cost is paid once.
    /// </summary>
    public async Task<JObject> DiscoverAsync(
        ScriptBase context,
        MissionControlOptions options,
        string query,
        string category = null)
    {
        var cacheKey = $"{query}|{category ?? ""}".ToLowerInvariant();

        // Check cache
        if (_chainCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            var cachedResult = cached.Result.DeepClone() as JObject;
            cachedResult["cached"] = true;
            return cachedResult;
        }

        try
        {
            var endpoint = options.McpChainEndpoint;
            var toolName = options.McpChainToolName;
            var prefix = options.McpChainQueryPrefix ?? "";

            var enhancedQuery = string.IsNullOrWhiteSpace(prefix)
                ? query
                : $"{prefix} {category ?? ""} API {query}".Trim();

            var toolResponse = await CallUpstreamToolAsync(context, endpoint, toolName, enhancedQuery)
                .ConfigureAwait(false);

            // Parse results and extract operations
            var operations = ExtractOperationsFromMcpResponse(toolResponse);

            var result = new JObject
            {
                ["success"] = true,
                ["operationCount"] = operations.Count,
                ["operations"] = operations,
                ["cached"] = false
            };

            // Cache result
            _chainCache[cacheKey] = new CacheEntry
            {
                Result = result.DeepClone() as JObject,
                Expiry = DateTime.UtcNow.AddMinutes(options.CacheExpiryMinutes)
            };

            return result;
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = "mcp_chain_failed",
                ["message"] = ex.Message,
                ["friendlyMessage"] = "Failed to discover operations via external documentation. Try a different query.",
                ["suggestion"] = "Rephrase your query or try invoking a known endpoint directly."
            };
        }
    }

    /// <summary>
    /// Call a tool on the upstream server, negotiating the protocol era.
    ///
    /// Modern first: one tools/call with _meta, no handshake. If that comes back looking
    /// like a legacy server, run the initialize handshake and retry. The decision is a
    /// property of the endpoint rather than the request, so it is cached.
    /// </summary>
    private async Task<JObject> CallUpstreamToolAsync(
        ScriptBase context, string endpoint, string toolName, string query)
    {
        var arguments = new JObject { ["query"] = query };

        bool knownLegacy;
        if (!_endpointIsLegacy.TryGetValue(endpoint, out knownLegacy))
        {
            var modernResult = await SendMcpRequestAsync(
                context, endpoint, BuildToolCall(toolName, arguments, modern: true)).ConfigureAwait(false);

            if (!LooksLegacy(modernResult))
            {
                _endpointIsLegacy[endpoint] = false;
                return modernResult.Body;
            }

            _endpointIsLegacy[endpoint] = true;
        }
        else if (!knownLegacy)
        {
            var modernResult = await SendMcpRequestAsync(
                context, endpoint, BuildToolCall(toolName, arguments, modern: true)).ConfigureAwait(false);
            return modernResult.Body;
        }

        // Legacy path: handshake, acknowledge, then call.
        var initRequest = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JObject
            {
                ["protocolVersion"] = "2025-11-25",
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject
                {
                    ["name"] = "power-mission-control",
                    ["version"] = "1.0.0"
                }
            }
        };
        await SendMcpRequestAsync(context, endpoint, initRequest).ConfigureAwait(false);

        await SendMcpRequestAsync(context, endpoint,
            new JObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }).ConfigureAwait(false);

        var legacyResult = await SendMcpRequestAsync(
            context, endpoint, BuildToolCall(toolName, arguments, modern: false)).ConfigureAwait(false);

        return legacyResult.Body;
    }

    private static JObject BuildToolCall(string toolName, JObject arguments, bool modern)
    {
        var parameters = new JObject
        {
            ["name"] = toolName,
            ["arguments"] = arguments
        };

        if (modern)
        {
            parameters["_meta"] = new JObject
            {
                [McpKeys.ProtocolVersion] = "2026-07-28",
                [McpKeys.ClientCapabilities] = new JObject(),
                [McpKeys.ClientInfo] = new JObject
                {
                    ["name"] = "power-mission-control",
                    ["version"] = "1.0.0"
                }
            };
        }

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["method"] = "tools/call",
            ["params"] = parameters
        };
    }

    /// <summary>
    /// Decide whether a failed modern call came from a legacy server. A recognized modern
    /// JSON-RPC error means the server is modern and simply refused this request, so we
    /// must not fall back. Anything else on a 4xx points at a legacy server.
    /// </summary>
    private static bool LooksLegacy(McpUpstreamResponse response)
    {
        if (response.IsSuccess) return false;

        var code = response.Body?["error"]?["code"]?.Value<int>();

        // -32022 UnsupportedProtocolVersion or -32020 HeaderMismatch: definitely modern.
        if (code == -32022 || code == -32020 || code == -32021) return false;

        return response.StatusCode >= 400 && response.StatusCode < 500;
    }

    private async Task<McpUpstreamResponse> SendMcpRequestAsync(ScriptBase context, string endpoint, JObject request)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(request.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        // 2026-07-28 requires these on Streamable HTTP so gateways can route on headers.
        var method = request.Value<string>("method");
        if (!string.IsNullOrWhiteSpace(method))
            httpRequest.Headers.TryAddWithoutValidation("Mcp-Method", method);

        var targetName = (request["params"] as JObject)?.Value<string>("name");
        if (!string.IsNullOrWhiteSpace(targetName))
            httpRequest.Headers.TryAddWithoutValidation("Mcp-Name", targetName);

        var metaVersion = (request["params"] as JObject)?["_meta"]?[McpKeys.ProtocolVersion]?.ToString();
        if (!string.IsNullOrWhiteSpace(metaVersion))
            httpRequest.Headers.TryAddWithoutValidation("MCP-Protocol-Version", metaVersion);

        var response = await context.Context.SendAsync(httpRequest, context.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject body;
        try { body = JObject.Parse(content); }
        catch { body = new JObject { ["text"] = content }; }

        return new McpUpstreamResponse
        {
            StatusCode = (int)response.StatusCode,
            IsSuccess = response.IsSuccessStatusCode && body["error"] == null,
            Body = body
        };
    }

    /// <summary>
    /// Extract API operations from MCP tool response using regex patterns.
    /// Looks for HTTP method + endpoint path patterns in the response text.
    /// </summary>
    private JArray ExtractOperationsFromMcpResponse(JObject response)
    {
        var operations = new JArray();
        var contentArray = response?["result"]?["content"] as JArray;
        if (contentArray == null) return operations;

        var fullText = "";
        foreach (var content in contentArray)
        {
            if (content.Value<string>("type") == "text")
                fullText += content.Value<string>("text") + "\n";
        }

        // Regex patterns to extract HTTP method + endpoint from documentation
        var patterns = new[]
        {
            @"(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)",
            @"(?:endpoint|path|url|route):\s*[`""']?(/[\w\{\}/\-\.]+)[`""']?",
            @"```\s*(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)"
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(fullText, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string httpMethod, path;
                if (match.Groups.Count >= 3)
                {
                    httpMethod = match.Groups[1].Value.ToUpperInvariant();
                    path = match.Groups[2].Value;
                }
                else
                {
                    httpMethod = "GET";
                    path = match.Groups[1].Value;
                }

                var key = $"{httpMethod} {path}";
                if (seen.Contains(key)) continue;
                seen.Add(key);

                operations.Add(new JObject
                {
                    ["endpoint"] = path,
                    ["method"] = httpMethod,
                    ["description"] = $"{httpMethod} {path}",
                    ["source"] = "documentation"
                });
            }
        }

        return operations;
    }
}

// ── Discovery Engine ─────────────────────────────────────────────────────────

public class DiscoveryEngine
{
    private readonly MissionControlOptions _options;
    private readonly CapabilityIndex _index;
    private readonly McpChainClient _chainClient;
    private readonly ApiProxy _proxy;

    private static readonly Dictionary<string, CacheEntry> _describeCache =
        new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

    public DiscoveryEngine(MissionControlOptions options, CapabilityIndex index, ApiProxy proxy, McpChainClient chainClient = null)
    {
        _options = options;
        _index = index;
        _proxy = proxy;
        _chainClient = chainClient;
    }

    /// <summary>
    /// Discover capabilities based on query and discovery mode.
    /// Returns matching operations with optional schemas.
    /// </summary>
    public async Task<JObject> DiscoverAsync(
        ScriptBase context,
        string query,
        string domain = null,
        bool includeSchema = false)
    {
        switch (_options.DiscoveryMode)
        {
            case DiscoveryMode.Static:
                return DiscoverStatic(query, domain, includeSchema);

            case DiscoveryMode.Hybrid:
                return await DiscoverHybridAsync(context, query, domain, includeSchema).ConfigureAwait(false);

            case DiscoveryMode.McpChain:
                if (_chainClient == null)
                    return new JObject { ["success"] = false, ["error"] = "McpChainClient not configured" };
                return await _chainClient.DiscoverAsync(context, _options, query, domain).ConfigureAwait(false);

            default:
                return DiscoverStatic(query, domain, includeSchema);
        }
    }

    private JObject DiscoverStatic(string query, string domain, bool includeSchema)
    {
        if (_index == null)
            return new JObject { ["success"] = false, ["error"] = "No capability index configured" };

        var matches = _index.Search(query, domain, _options.MaxDiscoverResults);
        var operations = new JArray();

        foreach (var entry in matches)
        {
            var op = new JObject
            {
                ["cid"] = entry.Cid,
                ["endpoint"] = entry.Endpoint,
                ["method"] = entry.Method,
                ["outcome"] = entry.Outcome,
                ["domain"] = entry.Domain
            };

            if (entry.RequiredParams != null && entry.RequiredParams.Length > 0)
                op["requiredParams"] = new JArray(entry.RequiredParams);
            if (entry.OptionalParams != null && entry.OptionalParams.Length > 0)
                op["optionalParams"] = new JArray(entry.OptionalParams);

            if (includeSchema && !string.IsNullOrWhiteSpace(entry.SchemaJson))
            {
                try { op["inputSchema"] = JObject.Parse(entry.SchemaJson); }
                catch { op["inputSchema"] = entry.SchemaJson; }
            }

            operations.Add(op);
        }

        return new JObject
        {
            ["success"] = true,
            ["operationCount"] = operations.Count,
            ["totalCapabilities"] = _index.Count,
            ["operations"] = operations,
            ["cached"] = false
        };
    }

    private async Task<JObject> DiscoverHybridAsync(
        ScriptBase context, string query, string domain, bool includeSchema)
    {
        // Start with static index search
        var result = DiscoverStatic(query, domain, includeSchema);
        if (!result.Value<bool>("success")) return result;

        // If include_schema is requested and we have a describe endpoint pattern,
        // fetch live schemas for matched operations
        if (includeSchema && !string.IsNullOrWhiteSpace(_options.DescribeEndpointPattern))
        {
            var operations = result["operations"] as JArray;
            if (operations != null)
            {
                foreach (var op in operations)
                {
                    var endpoint = op.Value<string>("endpoint") ?? "";
                    var resource = ExtractResourceFromEndpoint(endpoint);
                    if (string.IsNullOrWhiteSpace(resource)) continue;

                    // Check describe cache
                    var describeCacheKey = $"describe:{resource}";
                    if (_describeCache.TryGetValue(describeCacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
                    {
                        op["liveSchema"] = cached.Result.DeepClone();
                        op["liveSchemaSource"] = "cache";
                        continue;
                    }

                    // Fetch live schema from describe endpoint
                    try
                    {
                        var describePath = _options.DescribeEndpointPattern.Replace("{resource}", resource);
                        var describeResult = await _proxy.InvokeAsync(context, describePath, "GET").ConfigureAwait(false);

                        if (describeResult.Value<bool?>("success") == true)
                        {
                            var describeData = describeResult["data"];
                            op["liveSchema"] = describeData;
                            op["liveSchemaSource"] = "live";

                            // Cache describe result
                            _describeCache[describeCacheKey] = new CacheEntry
                            {
                                Result = (describeData as JObject)?.DeepClone() as JObject ?? new JObject(),
                                Expiry = DateTime.UtcNow.AddMinutes(_options.DescribeCacheTTL)
                            };
                        }
                    }
                    catch { /* Describe failed, proceed with static schema only */ }
                }
            }
        }

        return result;
    }

    /// <summary>Extract resource name from an endpoint path (e.g., "/items/{id}" → "items").</summary>
    private static string ExtractResourceFromEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        var segments = endpoint.Trim('/').Split('/');
        // Find the first non-parameter segment
        foreach (var seg in segments)
        {
            if (!seg.StartsWith("{") && !string.IsNullOrWhiteSpace(seg))
                return seg;
        }
        return null;
    }
}

// ── MissionControl (Registration) ────────────────────────────────────────────

public static class MissionControl
{
    /// <summary>
    /// Register scan, launch, and sequence tools on the MCP handler.
    /// This is the main entry point for mission control mode.
    /// </summary>
    public static void RegisterMission(
        McpRequestHandler handler,
        MissionControlOptions options,
        string capabilityIndexJson,
        ScriptBase context)
    {
        var index = !string.IsNullOrWhiteSpace(capabilityIndexJson)
            ? new CapabilityIndex(capabilityIndexJson)
            : null;

        var proxy = new ApiProxy(options);
        var chainClient = options.DiscoveryMode == DiscoveryMode.McpChain ? new McpChainClient() : null;
        var discovery = new DiscoveryEngine(options, index, proxy, chainClient);

        var serviceName = options.ServiceName ?? "api";

        // ── scan_{service} ────────────────────────────────────────────────

        var scanDescription = $"Scan for available {serviceName} operations matching your intent. " +
            $"Always call this before launch_{serviceName} to find the correct endpoint and required parameters. " +
            $"Returns operation summaries with endpoints, methods, and descriptions. " +
            $"Use include_schema=true to get full input parameter details for a specific operation.";

        handler.AddTool($"scan_{serviceName}", scanDescription,
            schema: s => s
                .String("query", "Natural language description of what you want to do (e.g., 'create a customer', 'list orders')", required: true)
                .String("domain", $"Filter by domain category (optional)", required: false)
                .Boolean("include_schema", "Set true to include full input parameter schemas in the results (costs more tokens)", required: false),
            handler: async (args, ct) =>
            {
                var query = args.Value<string>("query") ?? "";
                var domain = args.Value<string>("domain");
                var includeSchema = args.Value<bool?>("include_schema") ?? false;

                return await discovery.DiscoverAsync(context, query, domain, includeSchema).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── launch_{service} ─────────────────────────────────────────────

        var launchDescription = $"Launch a {serviceName} API operation. " +
            $"Use scan_{serviceName} first to find the correct endpoint. " +
            $"Replace any {{id}} placeholders in the endpoint with actual values.";

        handler.AddTool($"launch_{serviceName}", launchDescription,
            schema: s => s
                .String("endpoint", "API endpoint path (e.g., '/customers', '/orders/123')", required: true)
                .String("method", "HTTP method", required: true, enumValues: new[] { "GET", "POST", "PATCH", "PUT", "DELETE" })
                .Object("body", "Request body for POST/PATCH/PUT operations", nested => { }, required: false)
                .Object("query_params", "Query parameters as key-value pairs", nested => { }, required: false)
                .String("api_version", "API version override (optional)", required: false),
            handler: async (args, ct) =>
            {
                var endpoint = args.Value<string>("endpoint") ?? "";
                var method = args.Value<string>("method") ?? "GET";
                var body = args["body"] as JObject;
                var queryParams = args["query_params"] as JObject;
                var apiVersion = args.Value<string>("api_version");

                return await proxy.InvokeAsync(context, endpoint, method, body, queryParams, apiVersion, index).ConfigureAwait(false);
            });

        // ── sequence_{service} ───────────────────────────────────────────

        var batchDescription = $"Launch a sequence of multiple {serviceName} API operations in a single call. " +
            $"Maximum {options.MaxBatchSize} requests per sequence. " +
            $"Each request needs an id, endpoint, and method.";

        handler.AddTool($"sequence_{serviceName}", batchDescription,
            schema: s => s
                .Array("requests", "Array of API requests to execute",
                    itemSchema: new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["id"] = new JObject { ["type"] = "string", ["description"] = "Unique identifier for this request" },
                            ["endpoint"] = new JObject { ["type"] = "string", ["description"] = "API endpoint path" },
                            ["method"] = new JObject { ["type"] = "string", ["description"] = "HTTP method (GET, POST, PATCH, PUT, DELETE)" },
                            ["body"] = new JObject { ["type"] = "object", ["description"] = "Request body (for POST/PATCH/PUT)" },
                            ["query_params"] = new JObject { ["type"] = "object", ["description"] = "Query parameters" }
                        },
                        ["required"] = new JArray("endpoint", "method")
                    }, required: true)
                .String("api_version", "API version override (optional)", required: false),
            handler: async (args, ct) =>
            {
                var requests = args["requests"] as JArray;
                var apiVersion = args.Value<string>("api_version");

                return await proxy.BatchInvokeAsync(context, requests, apiVersion, index).ConfigureAwait(false);
            });
    }
}

// ── Error Handling ───────────────────────────────────────────────────────────

/// <summary>JSON-RPC 2.0 and MCP error codes.</summary>
public enum McpErrorCode
{
    // JSON-RPC 2.0.
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,

    // Legacy implementation-defined range (-32000 to -32019). 2026-07-28 forbids
    // allocating new codes here; this one predates the policy.
    RequestTimeout = -32000,

    // Reserved for the MCP specification (-32020 to -32099).
    HeaderMismatch = -32020,
    MissingRequiredClientCapability = -32021,
    UnsupportedProtocolVersion = -32022
}

public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

/// <summary>
/// Fluent builder for the JSON Schema objects used in tool inputSchema and outputSchema.
///
/// Deliberately narrower than JSON Schema 2020-12 permits. Copilot Studio drops any tool
/// whose schema contains a reference type, and truncates schemas that use multi-type
/// arrays, so this builder never emits $ref, $defs, or "type": [...]. Nested objects are
/// inlined instead. If you hand-write a schema, keep to the same rules — and if you add
/// numeric bounds, note that Copilot Studio expects the draft-04 boolean form of
/// exclusiveMinimum, not the 2020-12 numeric form.
/// </summary>
public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = _properties };

        // Recommended empty-schema form for a tool that takes no parameters.
        if (_properties.Count == 0) schema["additionalProperties"] = false;
        if (_required.Count > 0) schema["required"] = _required;

        return schema;
    }
}

// ── Internal Registration Classes ────────────────────────────────────────────

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<CancellationToken, Task<JArray>> Handler { get; set; }
}

internal class McpResourceTemplateDefinition
{
    public string UriTemplate { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<string, CancellationToken, Task<JArray>> Handler { get; set; }
}

public class McpPromptArgument
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
}

internal class McpPromptDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new List<McpPromptArgument>();
    public Func<JObject, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── McpRequestHandler ────────────────────────────────────────────────────────

public class McpRequestHandler
{
    private readonly McpServerOptions _options;

    // Dictionaries give O(1) dispatch. The parallel lists preserve registration order,
    // which 2026-07-28 asks for so clients can cache list results reliably.
    private readonly Dictionary<string, McpToolDefinition> _tools;
    private readonly List<McpToolDefinition> _toolOrder;
    private readonly Dictionary<string, McpResourceDefinition> _resources;
    private readonly List<McpResourceDefinition> _resourceOrder;
    private readonly List<McpResourceTemplateDefinition> _resourceTemplates;
    private readonly Dictionary<string, McpPromptDefinition> _prompts;
    private readonly List<McpPromptDefinition> _promptOrder;
    private readonly List<McpSkillDefinition> _skills;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
        _toolOrder = new List<McpToolDefinition>();
        _resources = new Dictionary<string, McpResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        _resourceOrder = new List<McpResourceDefinition>();
        _resourceTemplates = new List<McpResourceTemplateDefinition>();
        _prompts = new Dictionary<string, McpPromptDefinition>(StringComparer.OrdinalIgnoreCase);
        _promptOrder = new List<McpPromptDefinition>();
        _skills = new List<McpSkillDefinition>();
    }

    // ── Tool Registration ────────────────────────────────────────────────

    public McpRequestHandler AddTool(
        string name, string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annotationsObject = null;
        if (annotations != null) { annotationsObject = new JObject(); annotations(annotationsObject); }

        JObject outputSchema = null;
        if (outputSchemaConfig != null) { var ob = new McpSchemaBuilder(); outputSchemaConfig(ob); outputSchema = ob.Build(); }

        var definition = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotationsObject,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        McpToolDefinition previousTool;
        if (_tools.TryGetValue(name, out previousTool)) _toolOrder.Remove(previousTool);

        _tools[name] = definition;
        _toolOrder.Add(definition);
        return this;
    }

    // ── Resource Registration ─────────────────────────────────────────────

    public McpRequestHandler AddResource(
        string uri, string name, string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null) { annotations = new JObject(); annotationsConfig(annotations); }

        var definition = new McpResourceDefinition
        {
            Uri = uri, Name = name, Description = description,
            MimeType = mimeType, Annotations = annotations, Handler = handler
        };

        McpResourceDefinition previousResource;
        if (_resources.TryGetValue(uri, out previousResource)) _resourceOrder.Remove(previousResource);

        _resources[uri] = definition;
        _resourceOrder.Add(definition);
        return this;
    }

    public McpRequestHandler AddResourceTemplate(
        string uriTemplate, string name, string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null) { annotations = new JObject(); annotationsConfig(annotations); }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate, Name = name, Description = description,
            MimeType = mimeType, Annotations = annotations, Handler = handler
        });
        return this;
    }

    // ── Skill Registration ───────────────────────────────────────────────

    /// <summary>
    /// Publish an Agent Skill over MCP. The skill is exposed as ordinary MCP resources
    /// under the skill:// scheme, plus a generated skill://index.json discovery document
    /// that Agent Framework and Microsoft Foundry Toolbox read. Only the skill-md
    /// distribution type is produced.
    ///
    /// In mission control mode a skill is the natural place to document how the model
    /// should drive scan, launch, and sequence for a given business task.
    /// </summary>
    public McpRequestHandler AddSkill(
        string name,
        string description,
        string instructions,
        Action<McpSkillResourceBuilder> resources = null,
        string license = null,
        string compatibility = null,
        JObject metadata = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Skill name is required.");
        if (name.Length > 64)
            throw new ArgumentException($"Skill name '{name}' exceeds 64 characters.");
        if (!Regex.IsMatch(name, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            throw new ArgumentException($"Skill name '{name}' must use lowercase letters, digits, and single hyphens, with no leading or trailing hyphen.");
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException($"Skill '{name}' requires a description.");
        if (description.Length > 1024)
            throw new ArgumentException($"Skill '{name}' description exceeds 1024 characters.");
        if (!string.IsNullOrEmpty(compatibility) && compatibility.Length > 500)
            throw new ArgumentException($"Skill '{name}' compatibility exceeds 500 characters.");

        var resourceBuilder = new McpSkillResourceBuilder();
        resources?.Invoke(resourceBuilder);

        var skill = new McpSkillDefinition
        {
            Name = name,
            Description = description,
            Instructions = instructions,
            License = license,
            Compatibility = compatibility,
            Metadata = metadata,
            Resources = resourceBuilder.Resources
        };

        _skills.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        _skills.Add(skill);

        // The index handler runs at request time, so it always sees every skill
        // registered so far regardless of registration order.
        if (!_resources.ContainsKey(McpKeys.SkillIndexUri))
        {
            AddResource(McpKeys.SkillIndexUri, "Agent Skills index",
                "Discovery document listing the Agent Skills this server publishes.",
                handler: (ct) => Task.FromResult(BuildSkillIndexContents()));
        }

        AddResource(skill.SkillUri, name, description,
            handler: (ct) => Task.FromResult(new JArray
            {
                new JObject
                {
                    ["uri"] = skill.SkillUri,
                    ["mimeType"] = "text/markdown",
                    ["text"] = skill.RenderSkillMarkdown()
                }
            }),
            mimeType: "text/markdown");

        foreach (var resource in skill.Resources)
        {
            var uri = skill.ResourceUri(resource.Path);
            var captured = resource;

            AddResource(uri, resource.Path, resource.Description ?? resource.Path,
                handler: (ct) => Task.FromResult(new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = captured.MimeType,
                        ["text"] = captured.Content != null ? captured.Content() : string.Empty
                    }
                }),
                mimeType: resource.MimeType);
        }

        return this;
    }

    /// <summary>Build the skill://index.json contents from every registered skill.</summary>
    private JArray BuildSkillIndexContents()
    {
        var entries = new JArray();

        foreach (var skill in _skills)
        {
            var entry = new JObject
            {
                ["type"] = "skill-md",
                ["name"] = skill.Name,
                ["description"] = skill.Description,
                ["uri"] = skill.SkillUri
            };

            if (skill.Resources.Count > 0)
            {
                var resourceArray = new JArray();
                foreach (var resource in skill.Resources)
                {
                    resourceArray.Add(new JObject
                    {
                        ["path"] = resource.Path,
                        ["uri"] = skill.ResourceUri(resource.Path),
                        ["description"] = resource.Description,
                        ["mimeType"] = resource.MimeType
                    });
                }
                entry["resources"] = resourceArray;
            }

            entries.Add(entry);
        }

        return new JArray
        {
            new JObject
            {
                ["uri"] = McpKeys.SkillIndexUri,
                ["mimeType"] = "application/json",
                ["text"] = new JObject { ["skills"] = entries }.ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };
    }

    // ── Prompt Registration ──────────────────────────────────────────────

    public McpRequestHandler AddPrompt(
        string name, string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        _prompts[name] = new McpPromptDefinition
        {
            Name = name, Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };
        return this;
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    public Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        return HandleAsync(body, null, cancellationToken);
    }

    /// <summary>
    /// Process a request, using the Streamable HTTP headers to detect the protocol era
    /// and to check Mcp-Method / Mcp-Name against the body.
    /// </summary>
    public async Task<string> HandleAsync(string body, McpTransportHeaders headers, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try { request = JObject.Parse(body); }
        catch (JsonException) { return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON"); }

        var ctx = BuildContext(request, headers);

        Log("McpRequestReceived", new
        {
            Method = ctx.Method,
            HasId = ctx.Id != null,
            Era = ctx.IsModern ? "modern" : "legacy",
            ProtocolVersion = ctx.ProtocolVersion
        });

        // 2026-07-28 negotiates per request rather than once per session.
        if (ctx.IsModern && !IsSupportedVersion(ctx.ProtocolVersion))
        {
            Log("McpUnsupportedProtocolVersion", new { Requested = ctx.ProtocolVersion });
            return SerializeError(ctx.Id, McpErrorCode.UnsupportedProtocolVersion,
                "Unsupported protocol version",
                new JObject
                {
                    ["supported"] = new JArray(_options.SupportedProtocolVersions),
                    ["requested"] = ctx.ProtocolVersion
                });
        }

        var headerError = ValidateHeaders(ctx, request, headers);
        if (headerError != null) return headerError;

        try
        {
            switch (ctx.Method)
            {
                // Discovery — servers MUST implement this in 2026-07-28.
                case "server/discover": return HandleDiscover(ctx);

                // The legacy handshake. A client that opens this way is asking for legacy
                // semantics even if it also sent a version marker, so this never 404s.
                case "initialize": return HandleInitialize(ctx, request);

                case "initialized":
                case "notifications/initialized":
                    return SerializeSuccess(ctx, new JObject());

                // Valid in both eras. Copilot Studio expects a JSON-RPC body for every
                // request including notifications, so this always answers.
                case "notifications/cancelled":
                    return SerializeSuccess(ctx, new JObject());

                // Removed in 2026-07-28: ping and logging/setLevel are gone, and resource
                // subscriptions moved to the subscriptions/listen stream, which a
                // request/response connector cannot hold open.
                case "notifications/roots/list_changed":
                case "ping":
                case "logging/setLevel":
                case "resources/subscribe":
                case "resources/unsubscribe":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                case "tools/list": return HandleToolsList(ctx);
                case "tools/call": return await HandleToolsCallAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                case "resources/list": return HandleResourcesList(ctx);
                case "resources/templates/list": return HandleResourceTemplatesList(ctx);
                case "resources/read": return await HandleResourcesReadAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                case "prompts/list": return HandlePromptsList(ctx);
                case "prompts/get": return await HandlePromptsGetAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                case "completion/complete":
                    return SerializeSuccess(ctx, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });

                default:
                    Log("McpMethodNotFound", new { Method = ctx.Method });
                    return SerializeError(ctx.Id, McpErrorCode.MethodNotFound, "Method not found", ctx.Method);
            }
        }
        catch (McpException ex) { return SerializeError(ctx.Id, ex.Code, ex.Message); }
        catch (Exception ex) { return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message); }
    }

    // ── Era Detection ────────────────────────────────────────────────────

    /// <summary>
    /// Decide which protocol era this request belongs to and capture its metadata.
    ///
    /// The spec picks the era from how the client opens the conversation, so an
    /// initialize-style request is always legacy no matter what else it carries — a client
    /// mid-upgrade might send MCP-Protocol-Version while still using the handshake, and
    /// rejecting its initialize would break the connection for no benefit.
    ///
    /// Otherwise a request is modern when it carries a protocol version in _meta or in the
    /// MCP-Protocol-Version header, or when it calls server/discover. Everything else,
    /// Copilot Studio included, is served under legacy semantics.
    /// </summary>
    private McpRequestContext BuildContext(JObject request, McpTransportHeaders headers)
    {
        var paramsObj = request["params"] as JObject;
        var meta = paramsObj?["_meta"] as JObject;
        var method = request.Value<string>("method") ?? string.Empty;

        var metaVersion = meta?[McpKeys.ProtocolVersion]?.ToString();
        var version = !string.IsNullOrWhiteSpace(metaVersion) ? metaVersion : headers?.ProtocolVersion;

        var opensLegacy = method == "initialize"
            || method == "initialized"
            || method == "notifications/initialized";

        var isModern = !opensLegacy
            && (!string.IsNullOrWhiteSpace(version) || method == "server/discover");

        return new McpRequestContext
        {
            Id = request["id"],
            Method = method,
            IsModern = isModern,
            ProtocolVersion = !string.IsNullOrWhiteSpace(version) ? version : _options.ProtocolVersion,
            ClientInfo = meta?[McpKeys.ClientInfo] as JObject,
            ClientCapabilities = meta?[McpKeys.ClientCapabilities] as JObject,
            LogLevel = meta?[McpKeys.LogLevel]?.ToString(),
            InputResponses = paramsObj?["inputResponses"] as JObject,
            RequestState = paramsObj?["requestState"]?.ToString()
        };
    }

    private bool IsSupportedVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return true;

        var supported = _options.SupportedProtocolVersions;
        if (supported == null || supported.Count == 0)
            return string.Equals(version, _options.ProtocolVersion, StringComparison.Ordinal);

        foreach (var candidate in supported)
            if (string.Equals(candidate, version, StringComparison.Ordinal)) return true;

        return false;
    }

    /// <summary>
    /// 2026-07-28 requires Mcp-Method and Mcp-Name on Streamable HTTP so gateways can route
    /// without parsing the body. A header that disagrees with the body is a HeaderMismatch.
    /// An absent header is ignored: the Power Platform gateway makes no guarantee that it
    /// forwards custom headers, and failing closed would break every deployment.
    /// </summary>
    private string ValidateHeaders(McpRequestContext ctx, JObject request, McpTransportHeaders headers)
    {
        if (!ctx.IsModern || !_options.ValidateRequestHeaders || headers == null) return null;

        if (!string.IsNullOrWhiteSpace(headers.Method) &&
            !string.Equals(headers.Method, ctx.Method, StringComparison.Ordinal))
        {
            Log("McpHeaderMismatch", new { Header = headers.Method, Body = ctx.Method });
            return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                "Mcp-Method header does not match the request method",
                new JObject { ["header"] = headers.Method, ["body"] = ctx.Method });
        }

        if (!string.IsNullOrWhiteSpace(headers.Name))
        {
            var bodyName = (request["params"] as JObject)?.Value<string>("name");
            if (!string.IsNullOrWhiteSpace(bodyName) &&
                !string.Equals(headers.Name, bodyName, StringComparison.Ordinal))
            {
                Log("McpHeaderMismatch", new { Header = headers.Name, Body = bodyName });
                return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                    "Mcp-Name header does not match the request target",
                    new JObject { ["header"] = headers.Name, ["body"] = bodyName });
            }
        }

        return null;
    }

    /// <summary>Report a method that 2026-07-28 removed, rather than a bare not-found.</summary>
    private string MethodRemoved(McpRequestContext ctx, string method)
    {
        Log("McpMethodRemoved", new { Method = method, ProtocolVersion = ctx.ProtocolVersion });
        return SerializeError(ctx.Id, McpErrorCode.MethodNotFound,
            $"'{method}' was removed in MCP 2026-07-28", method);
    }

    // ── Protocol Handlers ────────────────────────────────────────────────

    /// <summary>
    /// server/discover — mandatory in 2026-07-28. Reports supported versions,
    /// capabilities, and identity in a single round trip.
    /// </summary>
    private string HandleDiscover(McpRequestContext ctx)
    {
        var result = new JObject
        {
            ["supportedVersions"] = new JArray(_options.SupportedProtocolVersions),
            ["capabilities"] = BuildCapabilities(isModern: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpDiscovered", new { Server = _options.ServerInfo.Name, Versions = _options.SupportedProtocolVersions.Count });
        return SerializeSuccess(ctx, result, McpCacheKind.Discover);
    }

    private JObject BuildCapabilities(bool isModern)
    {
        var capabilities = new JObject();

        if (_options.Capabilities.Tools) capabilities["tools"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Resources)
        {
            // subscribe is meaningless in the modern era without a subscriptions/listen stream.
            capabilities["resources"] = isModern
                ? new JObject { ["listChanged"] = false }
                : new JObject { ["subscribe"] = false, ["listChanged"] = false };
        }

        if (_options.Capabilities.Prompts) capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Completions) capabilities["completions"] = new JObject();

        // Logging is deprecated in 2026-07-28, so it is offered to legacy clients only.
        if (_options.Capabilities.Logging && !isModern) capabilities["logging"] = new JObject();

        if (_options.Capabilities.Extensions != null && _options.Capabilities.Extensions.Count > 0)
            capabilities["extensions"] = _options.Capabilities.Extensions;

        return capabilities;
    }

    private JObject BuildServerInfo(bool includeDetail)
    {
        var serverInfo = new JObject { ["name"] = _options.ServerInfo.Name, ["version"] = _options.ServerInfo.Version };
        if (!includeDetail) return serverInfo;

        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title)) serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description)) serverInfo["description"] = _options.ServerInfo.Description;
        if (_options.ServerInfo.Icons != null && _options.ServerInfo.Icons.Count > 0) serverInfo["icons"] = _options.ServerInfo.Icons;

        return serverInfo;
    }

    /// <summary>The legacy handshake, for clients on 2025-11-25 and earlier.</summary>
    private string HandleInitialize(McpRequestContext ctx, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString();
        var negotiated = !string.IsNullOrWhiteSpace(clientProtocolVersion) ? clientProtocolVersion : "2025-11-25";

        var result = new JObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = BuildCapabilities(isModern: false),
            ["serverInfo"] = BuildServerInfo(includeDetail: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions)) result["instructions"] = _options.Instructions;

        Log("McpInitialized", new { Server = _options.ServerInfo.Name, Version = _options.ServerInfo.Version, ProtocolVersion = negotiated });
        return SerializeSuccess(ctx, result);
    }

    private string HandleToolsList(McpRequestContext ctx)
    {
        var toolsArray = new JArray();
        foreach (var tool in _toolOrder)
        {
            var toolObj = new JObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = tool.InputSchema };
            if (!string.IsNullOrWhiteSpace(tool.Title)) toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null) toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0) toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }
        Log("McpToolsListed", new { Count = _toolOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["tools"] = toolsArray }, McpCacheKind.List);
    }

    private string HandleResourcesList(McpRequestContext ctx)
    {
        var arr = new JArray();
        foreach (var r in _resourceOrder)
        {
            var o = new JObject { ["uri"] = r.Uri, ["name"] = r.Name };
            if (!string.IsNullOrWhiteSpace(r.Description)) o["description"] = r.Description;
            if (!string.IsNullOrWhiteSpace(r.MimeType)) o["mimeType"] = r.MimeType;
            if (r.Annotations != null && r.Annotations.Count > 0) o["annotations"] = r.Annotations;
            arr.Add(o);
        }
        Log("McpResourcesListed", new { Count = _resourceOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["resources"] = arr }, McpCacheKind.List);
    }

    private string HandleResourceTemplatesList(McpRequestContext ctx)
    {
        var arr = new JArray();
        foreach (var t in _resourceTemplates)
        {
            var o = new JObject { ["uriTemplate"] = t.UriTemplate, ["name"] = t.Name };
            if (!string.IsNullOrWhiteSpace(t.Description)) o["description"] = t.Description;
            if (!string.IsNullOrWhiteSpace(t.MimeType)) o["mimeType"] = t.MimeType;
            if (t.Annotations != null && t.Annotations.Count > 0) o["annotations"] = t.Annotations;
            arr.Add(o);
        }
        Log("McpResourceTemplatesListed", new { Count = _resourceTemplates.Count });
        return SerializeSuccess(ctx, new JObject { ["resourceTemplates"] = arr }, McpCacheKind.List);
    }

    private async Task<string> HandleResourcesReadAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var uri = (request["params"] as JObject)?.Value<string>("uri");
        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Resource URI is required");

        if (_resources.TryGetValue(uri, out var resource))
        {
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);
                Log("McpResourceReadCompleted", new { Uri = uri });
                return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
            }
            catch (Exception ex)
            {
                Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
            }
        }

        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    Log("McpResourceReadCompleted", new { Uri = uri });
                    return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
                }
                catch (Exception ex)
                {
                    Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                    return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
                }
            }
        }

        // 2026-07-28 moved resource-not-found from -32002 to -32602 (Invalid Params).
        return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    private static bool MatchesUriTemplate(string template, string uri)
    {
        var tp = template.Split('/');
        var up = uri.Split('/');
        if (tp.Length != up.Length) return false;
        for (int i = 0; i < tp.Length; i++)
        {
            if (tp[i].StartsWith("{") && tp[i].EndsWith("}")) continue;
            if (!string.Equals(tp[i], up[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tp = template.Split('/');
        var up = uri.Split('/');
        if (tp.Length != up.Length) return result;
        for (int i = 0; i < tp.Length; i++)
        {
            if (tp[i].StartsWith("{") && tp[i].EndsWith("}"))
                result[tp[i].Substring(1, tp[i].Length - 2)] = up[i];
        }
        return result;
    }

    private string HandlePromptsList(McpRequestContext ctx)
    {
        var arr = new JArray();
        foreach (var p in _promptOrder)
        {
            var o = new JObject { ["name"] = p.Name };
            if (!string.IsNullOrWhiteSpace(p.Description)) o["description"] = p.Description;
            if (p.Arguments.Count > 0)
            {
                var args = new JArray();
                foreach (var a in p.Arguments)
                {
                    var ao = new JObject { ["name"] = a.Name };
                    if (!string.IsNullOrWhiteSpace(a.Description)) ao["description"] = a.Description;
                    if (a.Required) ao["required"] = true;
                    args.Add(ao);
                }
                o["arguments"] = args;
            }
            arr.Add(o);
        }
        Log("McpPromptsListed", new { Count = _promptOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["prompts"] = arr }, McpCacheKind.List);
    }

    private async Task<string> HandlePromptsGetAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var name = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(name))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Prompt name is required");
        if (!_prompts.TryGetValue(name, out var prompt))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Prompt not found: {name}");

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description)) result["description"] = prompt.Description;
            return SerializeSuccess(ctx, result);
        }
        catch (Exception ex) { return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message); }
    }

    private async Task<string> HandleToolsCallAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Tool name is required");
        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName, IsRetry = ctx.InputResponses != null });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            // Multi round-trip request: the tool needs more input before it can finish.
            if (result is JObject marker && marker.Value<bool?>("__mcpInputRequired") == true)
                return SerializeInputRequired(ctx, toolName, marker);

            JObject callResult;
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject { ["content"] = contentArray, ["isError"] = jobj.Value<bool?>("isError") ?? false };

                // structuredContent may be any JSON value as of 2026-07-28.
                if (jobj["structuredContent"] != null) callResult["structuredContent"] = jobj["structuredContent"];
            }
            else
            {
                string text;
                if (result is JObject po) text = po.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s) text = s;
                else text = result == null ? "{}" : JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName });
            return SerializeSuccess(ctx, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(ctx, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } }, ["isError"] = true });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(ctx, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } }, ["isError"] = true });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });
            return SerializeSuccess(ctx, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } }, ["isError"] = true });
        }
    }

    /// <summary>
    /// Emit an input_required result. Legacy clients cannot interpret resultType, so they
    /// receive a tool error naming what the tool still needs rather than a silent failure.
    /// </summary>
    private string SerializeInputRequired(McpRequestContext ctx, string toolName, JObject marker)
    {
        var inputRequests = marker["inputRequests"] as JObject ?? new JObject();

        if (!ctx.IsModern)
        {
            var needed = string.Join(", ", inputRequests.Properties().Select(p => p.Name));
            Log("McpInputRequiredUnsupported", new { Tool = toolName, Needed = needed });

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    TextContent($"'{toolName}' needs additional input ({needed}). That requires an MCP client on 2026-07-28 or later; supply the values as tool arguments instead.")
                },
                ["isError"] = true
            });
        }

        var result = new JObject { ["inputRequests"] = inputRequests };

        var requestState = marker.Value<string>("requestState");
        if (!string.IsNullOrWhiteSpace(requestState)) result["requestState"] = requestState;

        Log("McpInputRequired", new { Tool = toolName, Count = inputRequests.Count });
        return SerializeSuccess(ctx, result, McpCacheKind.None, "input_required");
    }

    // ── Content Helpers ──────────────────────────────────────────────────

    public static JObject TextContent(string text) => new JObject { ["type"] = "text", ["text"] = text };
    public static JObject ImageContent(string base64Data, string mimeType) => new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };
    public static JObject AudioContent(string base64Data, string mimeType) => new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };
    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject { ["type"] = "resource", ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType } };

    /// <summary>
    /// Create a resource link. Points the client at a resource it can fetch itself,
    /// instead of embedding the bytes in the tool result.
    /// </summary>
    public static JObject ResourceLinkContent(string uri, string name, string description = null, string mimeType = null)
    {
        var link = new JObject { ["type"] = "resource_link", ["uri"] = uri, ["name"] = name };
        if (!string.IsNullOrWhiteSpace(description)) link["description"] = description;
        if (!string.IsNullOrWhiteSpace(mimeType)) link["mimeType"] = mimeType;
        return link;
    }

    public static JObject ToolResult(JArray content, JToken structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── Multi Round-Trip Requests ────────────────────────────────────────

    /// <summary>
    /// Ask the client for more input before the tool can finish. Return this from a tool
    /// handler; the client answers by retrying the same tools/call with inputResponses
    /// attached, which arrive on McpRequestContext.InputResponses.
    ///
    /// This is how a stateless server performs elicitation as of 2026-07-28. Because the
    /// server holds no state between the two calls, encode anything you need to resume in
    /// requestState and read it back off the retry. In mission control mode this is the
    /// natural way to confirm a destructive launch before it executes.
    /// </summary>
    public static JObject InputRequired(JObject inputRequests, string requestState = null)
    {
        var result = new JObject
        {
            ["__mcpInputRequired"] = true,
            ["inputRequests"] = inputRequests ?? new JObject()
        };
        if (!string.IsNullOrWhiteSpace(requestState)) result["requestState"] = requestState;
        return result;
    }

    /// <summary>Build a single elicitation entry for an InputRequired result.</summary>
    public static JObject ElicitationRequest(string message, Action<McpSchemaBuilder> schemaConfig, string mode = "form")
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        return new JObject
        {
            ["method"] = "elicitation/create",
            ["params"] = new JObject
            {
                ["mode"] = mode,
                ["message"] = message,
                ["requestedSchema"] = builder.Build()
            }
        };
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(McpRequestContext ctx, JObject result) =>
        SerializeSuccess(ctx, result, McpCacheKind.None, "complete");

    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache) =>
        SerializeSuccess(ctx, result, cache, "complete");

    /// <summary>
    /// Envelope a successful result. Modern callers additionally get resultType, server
    /// identity in _meta, and cache hints. Legacy callers get exactly the shape they got
    /// before 2026-07-28, which is what keeps Copilot Studio working unchanged.
    /// </summary>
    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache, string resultType)
    {
        result = result ?? new JObject();

        if (ctx != null && ctx.IsModern)
        {
            result["resultType"] = resultType;

            var meta = result["_meta"] as JObject ?? new JObject();
            meta[McpKeys.ServerInfo] = BuildServerInfo(includeDetail: false);
            result["_meta"] = meta;

            // Only complete results are cacheable; input_required is interim.
            if (resultType == "complete") ApplyCacheHints(result, cache);
        }

        return new JObject { ["jsonrpc"] = "2.0", ["id"] = ctx?.Id, ["result"] = result }
            .ToString(Newtonsoft.Json.Formatting.None);
    }

    private void ApplyCacheHints(JObject result, McpCacheKind cache)
    {
        switch (cache)
        {
            case McpCacheKind.List:
                result["ttlMs"] = Math.Max(0, _options.ListCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;

            case McpCacheKind.Resource:
                result["ttlMs"] = Math.Max(0, _options.ResourceCacheTtlMs);
                result["cacheScope"] = _options.ResourceCacheScope ?? "private";
                break;

            case McpCacheKind.Discover:
                result["ttlMs"] = Math.Max(0, _options.DiscoverCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;
        }
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, JToken data = null)
    {
        var error = new JObject { ["code"] = (int)code, ["message"] = message };
        if (data != null && data.Type != JTokenType.Null) error["data"] = data;
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data) => OnLog?.Invoke(eventName, data);
}

/// <summary>Which cache hints a result should carry, per the 2026-07-28 CacheableResult rules.</summary>
public enum McpCacheKind
{
    None,
    List,
    Resource,
    Discover
}
