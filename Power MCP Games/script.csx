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
// ║  MCP Apps games: Snake, 2048, Minesweeper, and Tetris that an agent shows  ║
// ║  in chat. Each game is a ui:// resource served by this script; the play_*  ║
// ║  tools link to a game through _meta.ui.resourceUri and return the game's   ║
// ║  settings in structuredContent.                                            ║
// ║                                                                            ║
// ║  RegisterGameViews  - the four ui:// resources                             ║
// ║  RegisterGameTools  - the tools that open each game                        ║
// ║  GameViews          - the HTML, CSS, and JavaScript for each game          ║
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
            Name = "power-mcp-games",
            Version = "1.0.0",
            Title = "Power MCP Games",
            Description = "Snake, 2048, Minesweeper, and Tetris for agents, delivered as MCP Apps"
        },

        // Preferred revision. Legacy clients (Copilot Studio) that open with
        // initialize are detected and served under legacy semantics automatically.
        ProtocolVersion = "2026-07-28",
        SupportedProtocolVersions = new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" },

        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            // Declares MCP Apps support so a host that negotiates the extension can see it.
            Extensions = new JObject
            {
                [McpKeys.ExtensionUi] = new JObject { ["mimeTypes"] = new JArray(McpKeys.UiMimeType) }
            }
            // Logging is deprecated as of 2026-07-28; leave off for new servers.
        },

        // Cache hints attached to list and read results (2026-07-28 CacheableResult).
        ListCacheTtlMs = 300000,      // 5 minutes
        ListCacheScope = "public",
        ResourceCacheTtlMs = 60000,   // 1 minute
        ResourceCacheScope = "public", // Game HTML is identical for every caller
        DiscoverCacheTtlMs = 3600000, // 1 hour

        Instructions =
            "Use play_snake, play_2048, play_minesweeper, or play_tetris to show a game the user can play in the conversation. " +
            "The game is shown to the user, so reply in one short sentence and do not explain the rules at length. " +
            "If the user says they cannot see the game, tell them this host cannot display it and offer something else; " +
            "do not try to play the game in text. " +
            "When the user finishes a game, their score is added to the conversation, so you can congratulate them or offer another round."
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        // 1. Create the handler
        var handler = new McpRequestHandler(Options);

        // 2. Register tools and resources
        RegisterCapabilities(handler);

        // 3. Wire up logging (optional)
        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        // 4. Handle the request — one line does everything
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(
            body,
            McpTransportHeaders.FromRequest(this.Context.Request),
            this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        // 5. Return the response
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Capability Registration ─────────────────────────────────────────

    private void RegisterCapabilities(McpRequestHandler handler)
    {
        RegisterGameViews(handler);
        RegisterGameTools(handler);
    }

    // ── Game Views ─────────────────────────────────────────────────────

    private const string SnakeUri = "ui://power-mcp-games/snake";
    private const string Game2048Uri = "ui://power-mcp-games/2048";
    private const string MinesweeperUri = "ui://power-mcp-games/minesweeper";
    private const string TetrisUri = "ui://power-mcp-games/tetris";

    /// <summary>
    /// Register the ui:// resources. No csp is declared, so the host applies its restrictive
    /// default: no network access and no external scripts. The games draw on a canvas or with
    /// plain elements and load nothing from the network.
    /// </summary>
    private void RegisterGameViews(McpRequestHandler handler)
    {
        handler.AddUiResource(SnakeUri, "Snake",
            "Classic Snake: steer the snake to eat food without hitting a wall or yourself.",
            GameViews.Snake, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(Game2048Uri, "2048",
            "Slide tiles so matching numbers combine. Reach 2048.",
            GameViews.Game2048, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(MinesweeperUri, "Minesweeper",
            "Reveal every safe cell without hitting a mine.",
            GameViews.Minesweeper, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(TetrisUri, "Tetris",
            "Classic falling blocks: clear lines to score.",
            GameViews.Tetris, ui => ui["prefersBorder"] = false);
    }

    // ── Game Tools ─────────────────────────────────────────────────────
    //
    //    Each play_* tool opens a game. It returns a plain-text note in content for the
    //    model and for hosts that cannot render MCP Apps, and the game's settings in
    //    structuredContent for the game to read. Tool arguments are clamped here, so the
    //    schema stays flat and free of numeric bounds.

    private void RegisterGameTools(McpRequestHandler handler)
    {
        handler.AddTool("play_snake",
            "Play the classic Snake game. The user steers the snake with the arrow keys, swipes, or on-screen buttons, " +
            "eats food to grow, and loses by hitting a wall or the snake itself.",
            schema: s => s
                .String("speed", "Game speed. Defaults to normal.", enumValues: new[] { "slow", "normal", "fast" }),
            handler: (args, ct) =>
            {
                var speed = GetChoice(args, "speed", "normal", "slow", "normal", "fast");
                var interval = speed == "slow" ? 200 : speed == "fast" ? 80 : 120;
                var data = new JObject { ["game"] = "snake", ["speed"] = speed, ["interval"] = interval };
                return Task.FromResult(GameResult(GameText("Snake", " at " + speed + " speed"), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Snake",
            uiResourceUri: SnakeUri);

        handler.AddTool("play_2048",
            "Play the 2048 puzzle game. The user slides tiles with the arrow keys, swipes, or on-screen buttons; " +
            "matching numbers combine, and the goal is to reach 2048.",
            schema: s => s
                .Integer("gridSize", "Grid size from 3 to 6. Defaults to 4."),
            handler: (args, ct) =>
            {
                var size = GetInt(args, "gridSize", 4, 3, 6);
                var data = new JObject { ["game"] = "2048", ["gridSize"] = size };
                return Task.FromResult(GameResult(GameText("2048", " on a " + size + " by " + size + " grid"), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play 2048",
            uiResourceUri: Game2048Uri);

        handler.AddTool("play_minesweeper",
            "Play Minesweeper. The user reveals cells and flags mines, and wins by revealing every safe cell.",
            schema: s => s
                .String("difficulty", "Difficulty. Easy is 9 by 9 with 10 mines, medium is 16 by 16 with 40, hard is 30 by 16 with 99. Defaults to easy.",
                    enumValues: new[] { "easy", "medium", "hard" }),
            handler: (args, ct) =>
            {
                var difficulty = GetChoice(args, "difficulty", "easy", "easy", "medium", "hard");
                var width = difficulty == "hard" ? 30 : difficulty == "medium" ? 16 : 9;
                var height = difficulty == "easy" ? 9 : 16;
                var mines = difficulty == "hard" ? 99 : difficulty == "medium" ? 40 : 10;
                var data = new JObject
                {
                    ["game"] = "minesweeper",
                    ["difficulty"] = difficulty,
                    ["width"] = width,
                    ["height"] = height,
                    ["mines"] = mines
                };
                return Task.FromResult(GameResult(GameText("Minesweeper", " on " + difficulty + " (" + mines + " mines)"), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Minesweeper",
            uiResourceUri: MinesweeperUri);

        handler.AddTool("play_tetris",
            "Play Tetris. The user moves and rotates falling blocks with the arrow keys or on-screen buttons and clears lines to score.",
            schema: s => s
                .Integer("level", "Starting level from 1 to 20. A higher level is faster. Defaults to 1."),
            handler: (args, ct) =>
            {
                var level = GetInt(args, "level", 1, 1, 20);
                var data = new JObject { ["game"] = "tetris", ["level"] = level };
                return Task.FromResult(GameResult(GameText("Tetris", " starting at level " + level), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Tetris",
            uiResourceUri: TetrisUri);
    }

    // ── Game Helpers ────────────────────────────────────────────────────

    /// <summary>Tool result with a text note for the model and plain-text hosts, and the game's settings for the view.</summary>
    private static JObject GameResult(string text, JObject data)
    {
        return McpRequestHandler.ToolResult(new JArray { McpRequestHandler.TextContent(text) }, data);
    }

    private static string GameText(string name, string detail)
    {
        return name + " is shown to the user" + detail + ". Keep your reply to one short sentence and do not explain the rules at length. " +
            "If the user says they cannot see the game, tell them this host cannot display it and offer something else; do not try to play it in text.";
    }

    /// <summary>Read a whole-number argument, falling back to a default and clamping to a range.</summary>
    private static int GetInt(JObject args, string name, int defaultValue, int min, int max)
    {
        double parsed;
        try { parsed = args?[name] == null ? defaultValue : args[name].Value<double>(); }
        catch (Exception) { parsed = defaultValue; }
        return Math.Max(min, Math.Min(max, (int)Math.Round(parsed)));
    }

    /// <summary>Read a string argument that must be one of the allowed values, falling back to a default.</summary>
    private static string GetChoice(JObject args, string name, string defaultValue, params string[] allowed)
    {
        var raw = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        raw = raw.Trim().ToLowerInvariant();
        return Array.IndexOf(allowed, raw) >= 0 ? raw : defaultValue;
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
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = propsDict
                    }
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
        catch
        {
            // Suppress telemetry errors
        }
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
// ║  SECTION 2: MCP FRAMEWORK                                                  ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power       ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section   ║
// ║  becomes a using statement instead of inline code.                          ║
// ║                                                                            ║
// ║  Spec coverage: MCP 2026-07-28, dual-era                                   ║
// ║                                                                            ║
// ║  2026-07-28 removed the initialize handshake and made MCP stateless: every ║
// ║  request carries its own protocol version, identity, and capabilities in   ║
// ║  _meta. Power Platform connectors are stateless by construction, so the    ║
// ║  runtime and the protocol now agree rather than fight.                     ║
// ║                                                                            ║
// ║  This handler serves BOTH eras from one endpoint:                          ║
// ║    modern (2026-07-28)  server/discover, per-request _meta, resultType,    ║
// ║                         ttlMs/cacheScope, MRTR input_required              ║
// ║    legacy (2025-11-25-) initialize, ping, logging/setLevel,                ║
// ║                         resources/subscribe                                ║
// ║                                                                            ║
// ║  The era is chosen per request. Copilot Studio is a legacy client and is   ║
// ║  answered exactly as it was before this revision.                          ║
// ║                                                                            ║
// ║  Out of reach for a request/response connector (no open stream):           ║
// ║   - subscriptions/listen and every server-to-client notification           ║
// ║   - the io.modelcontextprotocol/tasks extension (needs durable state)      ║
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

    /// <summary>MIME type that marks a resource as an MCP Apps view.</summary>
    public const string UiMimeType = "text/html;profile=mcp-app";

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

/// <summary>
/// Throw from tool methods to surface a structured MCP error.
/// Mirrors ModelContextProtocol.McpException from the official SDK.
/// </summary>
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
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };

        // Recommended empty-schema form for a tool that takes no parameters.
        if (_properties.Count == 0) schema["additionalProperties"] = false;
        if (_required.Count > 0) schema["required"] = _required;

        return schema;
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

// ── Internal Tool Registration ───────────────────────────────────────────────

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public JObject Meta { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

// ── Internal Resource Registration ───────────────────────────────────────────

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public JObject Meta { get; set; }
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

// ── Internal Prompt Registration ─────────────────────────────────────────────

/// <summary>Describes a single prompt argument.</summary>
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
//
//    The core bridge class. Stateless, no DI, no ASP.NET Core.
//    Takes a JSON-RPC string in → returns a JSON-RPC string out.
//    This is the class that does not exist in the official SDK today.
//

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns
/// to Power Platform's ScriptBase.ExecuteAsync() model.
/// 
/// Handles all JSON-RPC 2.0 routing, protocol negotiation, tool discovery,
/// parameter binding, and response formatting internally.
/// </summary>
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

    /// <summary>
    /// Optional logging callback. Wire this up to Application Insights,
    /// Context.Logger, or any other telemetry sink.
    /// </summary>
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

    /// <summary>
    /// Register a tool using the fluent API.
    /// Define the schema with McpSchemaBuilder, provide a handler, and optionally set annotations.
    /// </summary>
    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null,
        string uiResourceUri = null,
        bool appOnly = false)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annotationsObject = null;
        if (annotations != null)
        {
            annotationsObject = new JObject();
            annotations(annotationsObject);
        }

        JObject outputSchema = null;
        if (outputSchemaConfig != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchemaConfig(outBuilder);
            outputSchema = outBuilder.Build();
        }

        var definition = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotationsObject,
            Meta = BuildToolUiMeta(uiResourceUri, appOnly),
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        McpToolDefinition previous;
        if (_tools.TryGetValue(name, out previous)) _toolOrder.Remove(previous);

        _tools[name] = definition;
        _toolOrder.Add(definition);

        return this;
    }

    /// <summary>
    /// Build the tool _meta that links a tool to its MCP Apps view. appOnly hides the tool
    /// from the model so only the view can call it (for example a card's submit action).
    /// The flat "ui/resourceUri" key is the pre-GA form some hosts still read.
    /// </summary>
    private static JObject BuildToolUiMeta(string uiResourceUri, bool appOnly)
    {
        if (string.IsNullOrWhiteSpace(uiResourceUri)) return null;

        var ui = new JObject { ["resourceUri"] = uiResourceUri };
        if (appOnly) ui["visibility"] = new JArray("app");

        return new JObject { ["ui"] = ui, ["ui/resourceUri"] = uiResourceUri };
    }

    // ── Resource Registration ─────────────────────────────────────────────

    /// <summary>
    /// Register an MCP Apps view. The HTML is served from resources/read with the
    /// text/html;profile=mcp-app MIME type, and uiMeta (csp, permissions, prefersBorder)
    /// is attached under _meta.ui. A view with no csp gets the host's restrictive default:
    /// no network access and no external scripts, which is what these views are written for.
    /// </summary>
    public McpRequestHandler AddUiResource(
        string uri,
        string name,
        string description,
        Func<string> html,
        Action<JObject> uiMeta = null)
    {
        var ui = new JObject();
        uiMeta?.Invoke(ui);

        return AddResource(
            uri, name, description,
            handler: ct => Task.FromResult(new JArray
            {
                new JObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = McpKeys.UiMimeType,
                    ["text"] = html()
                }
            }),
            mimeType: McpKeys.UiMimeType,
            meta: m => m["ui"] = ui);
    }

    /// <summary>
    /// Register a static resource. The handler returns the resource contents
    /// as a JArray of {uri, text, mimeType} or {uri, blob, mimeType} objects.
    /// </summary>
    public McpRequestHandler AddResource(
        string uri,
        string name,
        string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null,
        Action<JObject> meta = null)
    {
        JObject metaObject = null;
        if (meta != null)
        {
            metaObject = new JObject();
            meta(metaObject);
        }

        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        var definition = new McpResourceDefinition
        {
            Uri = uri,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Meta = metaObject,
            Handler = handler
        };

        McpResourceDefinition previous;
        if (_resources.TryGetValue(uri, out previous)) _resourceOrder.Remove(previous);

        _resources[uri] = definition;
        _resourceOrder.Add(definition);

        return this;
    }

    /// <summary>
    /// Register a resource template. The handler receives the resolved URI
    /// and returns the resource contents as a JArray.
    /// </summary>
    public McpRequestHandler AddResourceTemplate(
        string uriTemplate,
        string name,
        string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Handler = handler
        });

        return this;
    }

    // ── Prompt Registration ──────────────────────────────────────────────

    /// <summary>
    /// Register a prompt. The handler receives the argument values as a JObject
    /// and returns a JArray of message objects ({role, content: {type, text}}).
    ///
    /// Copilot Studio does not consume MCP prompts. Register them for clients that do.
    /// </summary>
    public McpRequestHandler AddPrompt(
        string name,
        string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        var definition = new McpPromptDefinition
        {
            Name = name,
            Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };

        McpPromptDefinition previous;
        if (_prompts.TryGetValue(name, out previous)) _promptOrder.Remove(previous);

        _prompts[name] = definition;
        _promptOrder.Add(definition);

        return this;
    }

    // ── Skill Registration ───────────────────────────────────────────────

    /// <summary>
    /// Publish an Agent Skill over MCP.
    ///
    /// The skill is exposed as ordinary MCP resources under the skill:// scheme, and a
    /// skill://index.json discovery document is generated from every registered skill.
    /// Agent Framework (.NET UseMcpSkills, Python MCPSkillsSource) and Microsoft Foundry
    /// Toolbox read that index and fetch SKILL.md on demand. Only the skill-md
    /// distribution type is produced; archives are not supported.
    /// </summary>
    /// <param name="name">Lowercase letters, digits, and single hyphens. Max 64 characters.</param>
    /// <param name="description">What the skill does and when to use it. Max 1024 characters.</param>
    /// <param name="instructions">The markdown body of SKILL.md. Keep it under 500 lines.</param>
    /// <param name="resources">Sibling files the instructions reference.</param>
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

        var index = new JObject { ["skills"] = entries };

        return new JArray
        {
            new JObject
            {
                ["uri"] = McpKeys.SkillIndexUri,
                ["mimeType"] = "application/json",
                ["text"] = index.ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Process a raw JSON-RPC 2.0 request string and return a JSON-RPC response string.
    /// This is the single method that bridges the gap.
    /// </summary>
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
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON");
        }

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
                case "server/discover":
                    return HandleDiscover(ctx);

                // The legacy handshake. A client that opens this way is asking for legacy
                // semantics even if it also sent a version marker, so this never 404s.
                case "initialize":
                    return HandleInitialize(ctx, request);

                case "initialized":
                case "notifications/initialized":
                    return SerializeSuccess(ctx, new JObject());

                case "notifications/roots/list_changed":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Removed in 2026-07-28: ping and logging/setLevel are gone, and
                // resource subscriptions moved to the subscriptions/listen stream,
                // which a request/response connector cannot hold open.
                case "ping":
                case "logging/setLevel":
                case "resources/subscribe":
                case "resources/unsubscribe":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Valid in both eras. Copilot Studio expects a JSON-RPC body for every
                // request including notifications, so this always answers.
                case "notifications/cancelled":
                    return SerializeSuccess(ctx, new JObject());

                // Tools
                case "tools/list":
                    return HandleToolsList(ctx);

                case "tools/call":
                    return await HandleToolsCallAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Resources
                case "resources/list":
                    return HandleResourcesList(ctx);

                case "resources/templates/list":
                    return HandleResourceTemplatesList(ctx);

                case "resources/read":
                    return await HandleResourcesReadAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Prompts
                case "prompts/list":
                    return HandlePromptsList(ctx);

                case "prompts/get":
                    return await HandlePromptsGetAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Completions
                case "completion/complete":
                    return SerializeSuccess(ctx, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                default:
                    Log("McpMethodNotFound", new { Method = ctx.Method });
                    return SerializeError(ctx.Id, McpErrorCode.MethodNotFound, "Method not found", ctx.Method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = ctx.Method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(ctx.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = ctx.Method, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
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

        Log("McpDiscovered", new
        {
            Server = _options.ServerInfo.Name,
            Versions = _options.SupportedProtocolVersions.Count
        });

        return SerializeSuccess(ctx, result, McpCacheKind.Discover);
    }

    private JObject BuildCapabilities(bool isModern)
    {
        var capabilities = new JObject();

        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Resources)
        {
            // subscribe is meaningless in the modern era without a subscriptions/listen stream.
            capabilities["resources"] = isModern
                ? new JObject { ["listChanged"] = false }
                : new JObject { ["subscribe"] = false, ["listChanged"] = false };
        }

        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

        // Logging is deprecated in 2026-07-28, so it is offered to legacy clients only.
        if (_options.Capabilities.Logging && !isModern)
            capabilities["logging"] = new JObject();

        if (_options.Capabilities.Extensions != null && _options.Capabilities.Extensions.Count > 0)
            capabilities["extensions"] = _options.Capabilities.Extensions;

        return capabilities;
    }

    private JObject BuildServerInfo(bool includeDetail)
    {
        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };

        if (!includeDetail) return serverInfo;

        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;
        if (_options.ServerInfo.Icons != null && _options.ServerInfo.Icons.Count > 0)
            serverInfo["icons"] = _options.ServerInfo.Icons;

        return serverInfo;
    }

    /// <summary>The legacy handshake, for clients on 2025-11-25 and earlier.</summary>
    private string HandleInitialize(McpRequestContext ctx, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString();
        var negotiated = !string.IsNullOrWhiteSpace(clientProtocolVersion)
            ? clientProtocolVersion
            : "2025-11-25";

        var result = new JObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = BuildCapabilities(isModern: false),
            ["serverInfo"] = BuildServerInfo(includeDetail: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = negotiated
        });

        return SerializeSuccess(ctx, result);
    }

    private string HandleToolsList(McpRequestContext ctx)
    {
        var toolsArray = new JArray();
        foreach (var tool in _toolOrder)
        {
            var toolObj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema
            };
            if (!string.IsNullOrWhiteSpace(tool.Title))
                toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null)
                toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0)
                toolObj["annotations"] = tool.Annotations;
            if (tool.Meta != null && tool.Meta.Count > 0)
                toolObj["_meta"] = tool.Meta;
            toolsArray.Add(toolObj);
        }

        Log("McpToolsListed", new { Count = _toolOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["tools"] = toolsArray }, McpCacheKind.List);
    }

    private string HandleResourcesList(McpRequestContext ctx)
    {
        var resourcesArray = new JArray();
        foreach (var res in _resourceOrder)
        {
            var obj = new JObject
            {
                ["uri"] = res.Uri,
                ["name"] = res.Name
            };
            if (!string.IsNullOrWhiteSpace(res.Description))
                obj["description"] = res.Description;
            if (!string.IsNullOrWhiteSpace(res.MimeType))
                obj["mimeType"] = res.MimeType;
            if (res.Annotations != null && res.Annotations.Count > 0)
                obj["annotations"] = res.Annotations;
            if (res.Meta != null && res.Meta.Count > 0)
                obj["_meta"] = res.Meta;
            resourcesArray.Add(obj);
        }

        Log("McpResourcesListed", new { Count = _resourceOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["resources"] = resourcesArray }, McpCacheKind.List);
    }

    private string HandleResourceTemplatesList(McpRequestContext ctx)
    {
        var templatesArray = new JArray();
        foreach (var tmpl in _resourceTemplates)
        {
            var obj = new JObject
            {
                ["uriTemplate"] = tmpl.UriTemplate,
                ["name"] = tmpl.Name
            };
            if (!string.IsNullOrWhiteSpace(tmpl.Description))
                obj["description"] = tmpl.Description;
            if (!string.IsNullOrWhiteSpace(tmpl.MimeType))
                obj["mimeType"] = tmpl.MimeType;
            if (tmpl.Annotations != null && tmpl.Annotations.Count > 0)
                obj["annotations"] = tmpl.Annotations;
            templatesArray.Add(obj);
        }

        Log("McpResourceTemplatesListed", new { Count = _resourceTemplates.Count });
        return SerializeSuccess(ctx, new JObject { ["resourceTemplates"] = templatesArray }, McpCacheKind.List);
    }

    private async Task<string> HandleResourcesReadAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var uri = paramsObj?.Value<string>("uri");

        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Resource URI is required");

        // 1. Try exact match on registered static resources
        if (_resources.TryGetValue(uri, out var resource))
        {
            Log("McpResourceReadStarted", new { Uri = uri });
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);

                // The host reads csp and display hints from the content item, not the listing.
                if (resource.Meta != null)
                {
                    foreach (var item in contents.OfType<JObject>())
                        if (item["_meta"] == null) item["_meta"] = resource.Meta.DeepClone();
                }

                Log("McpResourceReadCompleted", new { Uri = uri });
                return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
            }
            catch (Exception ex)
            {
                Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
            }
        }

        // 2. Try matching against registered resource templates
        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                Log("McpResourceReadStarted", new { Uri = uri, Template = tmpl.UriTemplate });
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

    /// <summary>
    /// Simple URI template matcher. Checks if a concrete URI matches a template
    /// with {param} placeholders (e.g., "data://records/{id}" matches "data://records/123").
    /// </summary>
    private static bool MatchesUriTemplate(string template, string uri)
    {
        // Split both on '/' and compare segments
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return false;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}")) continue; // wildcard
            if (!string.Equals(seg, uriParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// Extract named parameters from a URI given a template pattern.
    /// E.g., template "data://records/{id}" with uri "data://records/123" returns { "id": "123" }.
    /// </summary>
    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return result;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}"))
            {
                var paramName = seg.Substring(1, seg.Length - 2);
                result[paramName] = uriParts[i];
            }
        }
        return result;
    }

    private string HandlePromptsList(McpRequestContext ctx)
    {
        var promptsArray = new JArray();
        foreach (var prompt in _promptOrder)
        {
            var obj = new JObject
            {
                ["name"] = prompt.Name
            };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                obj["description"] = prompt.Description;

            if (prompt.Arguments.Count > 0)
            {
                var argsArray = new JArray();
                foreach (var arg in prompt.Arguments)
                {
                    var argObj = new JObject { ["name"] = arg.Name };
                    if (!string.IsNullOrWhiteSpace(arg.Description))
                        argObj["description"] = arg.Description;
                    if (arg.Required)
                        argObj["required"] = true;
                    argsArray.Add(argObj);
                }
                obj["arguments"] = argsArray;
            }

            promptsArray.Add(obj);
        }

        Log("McpPromptsListed", new { Count = _promptOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["prompts"] = promptsArray }, McpCacheKind.List);
    }

    private async Task<string> HandlePromptsGetAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(promptName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Prompt name is required");

        if (!_prompts.TryGetValue(promptName, out var prompt))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Prompt not found: {promptName}");

        Log("McpPromptGetStarted", new { Prompt = promptName });

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            Log("McpPromptGetCompleted", new { Prompt = promptName, MessageCount = messages.Count });

            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                result["description"] = prompt.Description;

            return SerializeSuccess(ctx, result);
        }
        catch (Exception ex)
        {
            Log("McpPromptGetError", new { Prompt = promptName, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
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

            // Support pre-formatted MCP tool results with rich content types
            // (image, audio, resource, or mixed content arrays).
            // If the handler returns { "content": [ { "type": "..." } ], ... },
            // pass it through directly instead of wrapping in text.
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };

                // structuredContent may be any JSON value as of 2026-07-28.
                if (jobj["structuredContent"] != null)
                    callResult["structuredContent"] = jobj["structuredContent"];
            }
            else
            {
                string text;
                if (result is JObject plainObj)
                    text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s)
                    text = s;
                else if (result == null)
                    text = "{}";
                else
                    text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(ctx, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
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
        if (!string.IsNullOrWhiteSpace(requestState))
            result["requestState"] = requestState;

        Log("McpInputRequired", new { Tool = toolName, Count = inputRequests.Count });

        return SerializeSuccess(ctx, result, McpCacheKind.None, "input_required");
    }

    // ── Content Helpers ────────────────────────────────────────────────
    //
    //    Use these to build rich tool results with image, audio, or resource
    //    content. Return McpRequestHandler.ToolResult(...) from your handler
    //    to bypass automatic text wrapping.
    //

    /// <summary>Create a text content item.</summary>
    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    /// <summary>Create an image content item (base64-encoded).</summary>
    public static JObject ImageContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an audio content item (base64-encoded).</summary>
    public static JObject AudioContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an embedded resource content item.</summary>
    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType }
        };

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

    /// <summary>
    /// Build a pre-formatted tool result with mixed content types.
    /// Return this from a tool handler to bypass automatic text wrapping.
    /// </summary>
    public static JObject ToolResult(JArray content, JToken structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── Multi Round-Trip Requests ──────────────────────────────────────

    /// <summary>
    /// Ask the client for more input before the tool can finish. Return this from a tool
    /// handler; the client answers by retrying the same tools/call with inputResponses
    /// attached, which arrive on McpRequestContext.InputResponses.
    ///
    /// This is how a stateless server performs elicitation as of 2026-07-28. Because the
    /// server holds no state between the two calls, encode anything you need to resume in
    /// requestState and read it back off the retry.
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

    private string SerializeSuccess(McpRequestContext ctx, JObject result)
    {
        return SerializeSuccess(ctx, result, McpCacheKind.None, "complete");
    }

    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache)
    {
        return SerializeSuccess(ctx, result, cache, "complete");
    }

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

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ctx?.Id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
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
        var error = new JObject
        {
            ["code"] = (int)code,
            ["message"] = message
        };

        if (data != null && data.Type != JTokenType.Null)
            error["data"] = data;

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data)
    {
        OnLog?.Invoke(eventName, data);
    }
}

/// <summary>Which cache hints a result should carry, per the 2026-07-28 CacheableResult rules.</summary>
public enum McpCacheKind
{
    None,
    List,
    Resource,
    Discover
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 3: GAME VIEWS                                                     ║
// ║                                                                            ║
// ║  The HTML for each ui:// resource. Every game is one self-contained        ║
// ║  document: no CDN, no external fonts, no network calls, so it runs under   ║
// ║  the host's default content security policy.                               ║
// ║                                                                            ║
// ║  The games share the Fluent 2 styles and the host bridge used by the cards ║
// ║  template (BaseCss and Bridge below), so they follow the host's light and  ║
// ║  dark theme. Boards are drawn on a transparent canvas over a themed        ║
// ║  background, or built from elements styled with theme tokens.              ║
// ║                                                                            ║
// ║  Each game offers more than one way to play, because a card in a sandboxed ║
// ║  frame may never receive keyboard focus: arrow keys, swipes, and on-screen ║
// ║  buttons all work. Games pause when the card loses focus or is hidden, and ║
// ║  a finished game reports its score to the agent with ui/update-model-      ║
// ║  context.                                                                  ║
// ║                                                                            ║
// ║  The strings use single quotes only, because they are C# verbatim strings. ║
// ║  Every value from tool data reaches the page through textContent, never    ║
// ║  innerHTML.                                                                ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

internal static class GameViews
{
    private const string Loading =
        "<div id='root'><div class='card' aria-busy='true' aria-label='Loading'>" +
        "<div class='sk' style='width:40%'></div><div class='sk' style='width:85%'></div><div class='sk' style='width:65%'></div></div></div>";

    public static string Snake() { return Page("Snake", GameCss + SnakeCss, Loading, GameJs + SnakeJs); }
    public static string Game2048() { return Page("2048", GameCss + Game2048Css, Loading, GameJs + Game2048Js); }
    public static string Minesweeper() { return Page("Minesweeper", GameCss + MinesCss, Loading, GameJs + MinesJs); }
    public static string Tetris() { return Page("Tetris", GameCss + TetrisCss, Loading, GameJs + TetrisJs); }

    private static string Page(string title, string css, string body, string script)
    {
        return "<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'>" +
            "<meta name='viewport' content='width=device-width, initial-scale=1'>" +
            "<title>" + title + "</title><style>" + BaseCss + css + "</style></head><body>" +
            body + "<script>" + Bridge + script + "</script></body></html>";
    }

    // ── Shared styles ───────────────────────────────────────────────────

    private const string BaseCss = @"
/* Fluent 2 web tokens (light, dark). The host's --color-text-primary, --color-background-primary,
   --color-border-primary and --font-sans win when it provides them, so a card follows its host. */
:root{color-scheme:light dark;
--fg1:var(--color-text-primary,light-dark(#242424,#ffffff));
--fg2:light-dark(#424242,#d6d6d6);
--fg3:light-dark(#616161,#adadad);
--fgd:light-dark(#bdbdbd,#5c5c5c);
--bg1:var(--color-background-primary,light-dark(#ffffff,#292929));
--bg1h:light-dark(#f5f5f5,#3d3d3d);
--bg1p:light-dark(#e0e0e0,#1f1f1f);
--bg3:light-dark(#f5f5f5,#1f1f1f);
--bgs:light-dark(#ebebeb,#333333);
--bg6:light-dark(#e6e6e6,#3d3d3d);
--bgd:light-dark(#f0f0f0,#141414);
--s1:var(--color-border-primary,light-dark(#d1d1d1,#666666));
--s1h:light-dark(#c7c7c7,#757575);
--s2:light-dark(#e0e0e0,#525252);
--sacc:light-dark(#616161,#adadad);
--sd:light-dark(#e0e0e0,#424242);
--brand:light-dark(#0f6cbd,#115ea3);
--brandh:light-dark(#115ea3,#0f6cbd);
--brandp:#0c3b5e;
--brandfg:light-dark(#0f6cbd,#479ef5);
--focus:light-dark(#000000,#ffffff);
--red-fg:light-dark(#b10e1c,#dc626d);--red-bg:light-dark(#fdf3f4,#3f1011);--red-bd:light-dark(#eeacb2,#751d1f);
--grn-fg:light-dark(#0e700e,#54b054);--grn-bg:light-dark(#f1faf1,#052505);--grn-bd:light-dark(#9fd89f,#0e4d0e);
--org-fg:light-dark(#bc4b09,#f98845);--org-bg:light-dark(#fdf6f3,#411200);--org-bd:light-dark(#f4bfab,#7a3405);
--r-m:4px;--r-xl:8px;
--font:var(--font-sans,'Segoe UI','Segoe UI Web (West European)',-apple-system,BlinkMacSystemFont,Roboto,'Helvetica Neue',sans-serif);}
*{box-sizing:border-box}
html,body{margin:0}
body{padding:2px;background:transparent;color:var(--fg1);font-family:var(--font);font-size:14px;line-height:20px;font-weight:400;-webkit-font-smoothing:antialiased}
.card{background:var(--bg1);border:1px solid var(--s2);border-radius:var(--r-xl);padding:16px;max-width:720px}
html[data-mode=fullscreen] body{padding:0}
html[data-mode=fullscreen] .card{max-width:none;min-height:100vh;border:0;border-radius:0;padding:24px}
h1{margin:0;font-size:16px;line-height:22px;font-weight:600}
p{margin:0}
.stack>*+*{margin-top:12px}
.stack>div:empty{display:none}
.cap{font-size:12px;line-height:16px;color:var(--fg3)}
.row{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.row.between{justify-content:space-between}
.sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
.btn{display:inline-flex;align-items:center;justify-content:center;gap:6px;min-width:96px;height:32px;padding:0 12px;font:inherit;font-weight:600;color:var(--fg1);background:var(--bg1);border:1px solid var(--s1);border-radius:var(--r-m);cursor:pointer}
.btn:hover:not(:disabled){background:var(--bg1h);border-color:var(--s1h)}
.btn:active:not(:disabled){background:var(--bg1p)}
.btn.primary{color:#ffffff;background:var(--brand);border-color:transparent}
.btn.primary:hover:not(:disabled){background:var(--brandh);border-color:transparent}
.btn.primary:active:not(:disabled){background:var(--brandp)}
.btn:disabled{color:var(--fgd);background:var(--bgd);border-color:var(--sd);cursor:not-allowed}
.btn.sm{height:24px;min-width:0;padding:0 8px;font-size:12px}
.actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:16px}
input,select,textarea{font:inherit;color:var(--fg1);background:var(--bg1);border:1px solid var(--s1);border-bottom-color:var(--sacc);border-radius:var(--r-m);padding:0 10px;height:32px;width:100%}
textarea{height:auto;min-height:60px;padding:6px 10px;resize:vertical}
input:hover:not(:disabled),select:hover:not(:disabled),textarea:hover:not(:disabled){border-color:var(--s1h);border-bottom-color:var(--sacc)}
input:focus,select:focus,textarea:focus{outline:none;border-bottom-color:var(--brandfg);box-shadow:0 1px 0 0 var(--brandfg)}
input:disabled,select:disabled,textarea:disabled{color:var(--fgd);background:var(--bgd);border-color:var(--sd);cursor:not-allowed}
input::placeholder,textarea::placeholder{color:var(--fg3)}
input[type=checkbox]{width:16px;height:16px;padding:0;accent-color:var(--brand);cursor:pointer}
[aria-invalid=true]{border-color:var(--red-fg)}
.btn:focus-visible,th button:focus-visible,input[type=checkbox]:focus-visible{outline:2px solid var(--focus);outline-offset:1px}
label.lbl{display:block;margin-bottom:4px}
.req{color:var(--red-fg)}
.ic{flex:none;width:16px;height:16px}
.ic .mk{fill:none;stroke:var(--bg1);stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.ic .ln{fill:none;stroke:currentColor;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.rot{animation:rot 1s linear infinite}
.spin{width:16px;height:16px;border:2px solid currentColor;border-right-color:transparent;border-radius:50%;animation:rot .8s linear infinite}
@keyframes rot{to{transform:rotate(360deg)}}
.msg{display:flex;gap:8px;align-items:flex-start;padding:8px 12px;border:1px solid var(--s1);border-radius:var(--r-m);background:var(--bg3)}
.msg .body{flex:1;min-width:0}
.msg .t{font-weight:600}
.msg.error{background:var(--red-bg);border-color:var(--red-bd)}.msg.error>.ic{color:var(--red-fg)}
.msg.success{background:var(--grn-bg);border-color:var(--grn-bd)}.msg.success>.ic{color:var(--grn-fg)}
.msg.warning{background:var(--org-bg);border-color:var(--org-bd)}.msg.warning>.ic{color:var(--org-fg)}
.msg.info>.ic{color:var(--brandfg)}
.badge{display:inline-flex;align-items:center;height:20px;padding:0 8px;border:1px solid transparent;border-radius:999px;font-size:12px;line-height:16px;font-weight:600;text-transform:capitalize}
.sk{height:12px;border-radius:var(--r-m);background:linear-gradient(90deg,var(--bg6) 25%,var(--bg3) 50%,var(--bg6) 75%);background-size:300% 100%;animation:sh 1.5s ease-in-out infinite}
.sk+.sk{margin-top:12px}
@keyframes sh{from{background-position:100% 0}to{background-position:0 0}}
@media (prefers-reduced-motion:reduce){.sk,.rot,.spin{animation-duration:4s}}
";

    // ── Shared host bridge ──────────────────────────────────────────────

    private const string Bridge = @"
var App = (function () {
  var PROTOCOL = '2026-01-26';
  var nextId = 1, pending = {}, handlers = {}, caps = {}, mode = 'inline', modeListeners = [];
  var standalone = window.parent === window;
  var resizeTimer = null, lastHeight = 0;

  function post(message) { window.parent.postMessage(message, '*'); }

  function request(method, params) {
    if (standalone) return Promise.reject(new Error('Not running inside a host'));
    return new Promise(function (resolve, reject) {
      var id = nextId++;
      pending[id] = { resolve: resolve, reject: reject };
      post({ jsonrpc: '2.0', id: id, method: method, params: params || {} });
    });
  }

  function notify(method, params) {
    if (!standalone) post({ jsonrpc: '2.0', method: method, params: params || {} });
  }

  function applyContext(context) {
    if (!context) return;
    var root = document.documentElement;
    if (context.theme) {
      root.setAttribute('data-theme', context.theme);
      root.style.colorScheme = context.theme;
    }
    var vars = context.styles && context.styles.variables;
    if (vars) for (var name in vars) if (vars[name]) root.style.setProperty(name, vars[name]);
    var fonts = context.styles && context.styles.css && context.styles.css.fonts;
    if (fonts) { var style = document.createElement('style'); style.textContent = fonts; document.head.appendChild(style); }
    if (context.displayMode) setMode(context.displayMode);
  }

  function setMode(next) {
    if (next === mode) return;
    mode = next;
    document.documentElement.setAttribute('data-mode', mode);
    modeListeners.forEach(function (fn) { fn(mode); });
    resize();
  }

  function requestDisplayMode(next) {
    if (standalone) { setMode(next); return Promise.resolve(mode); }
    return request('ui/request-display-mode', { mode: next }).then(function (result) {
      if (result && result.mode) setMode(result.mode);
      return mode;
    });
  }

  window.addEventListener('message', function (event) {
    if (event.source !== window.parent) return;
    var message = event.data;
    if (!message || message.jsonrpc !== '2.0') return;

    if (message.method) {
      if (message.id !== undefined) {
        if (message.method === 'ping' || message.method === 'ui/resource-teardown') post({ jsonrpc: '2.0', id: message.id, result: {} });
        else post({ jsonrpc: '2.0', id: message.id, error: { code: -32601, message: 'Method not found' } });
      }
      var handler = handlers[message.method];
      if (handler) handler(message.params || {});
      return;
    }

    var waiting = pending[message.id];
    if (!waiting) return;
    delete pending[message.id];
    if (message.error) waiting.reject(new Error(message.error.message || 'Request failed'));
    else waiting.resolve(message.result);
  });

  handlers['ui/notifications/host-context-changed'] = applyContext;

  function measure() {
    var html = document.documentElement, previous = html.style.height;
    html.style.height = 'max-content';
    var height = Math.ceil(html.getBoundingClientRect().height);
    html.style.height = previous;
    return height;
  }

  function resize() {
    if (standalone || mode === 'fullscreen') return;
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () {
      var height = measure();
      if (height !== lastHeight) { lastHeight = height; notify('ui/notifications/size-changed', { height: height }); }
    }, 30);
  }

  function connect(name, modes) {
    if (standalone) return Promise.resolve(null);
    return request('ui/initialize', {
      protocolVersion: PROTOCOL,
      appInfo: { name: name, version: '1.0.0' },
      appCapabilities: { availableDisplayModes: modes || ['inline'] }
    }).then(function (result) {
      caps = (result && result.hostCapabilities) || {};
      applyContext(result && result.hostContext);
      notify('ui/notifications/initialized');
      if (window.ResizeObserver) new ResizeObserver(resize).observe(document.body);
      resize();
      return result;
    });
  }

  return {
    isStandalone: standalone,
    connect: connect,
    resize: resize,
    mode: function () { return mode; },
    onMode: function (fn) { modeListeners.push(fn); },
    requestDisplayMode: requestDisplayMode,
    onToolResult: function (fn) { handlers['ui/notifications/tool-result'] = fn; },
    onToolCancelled: function (fn) { handlers['ui/notifications/tool-cancelled'] = fn; },
    canCallTools: function () { return !!caps.serverTools; },
    callTool: function (name, args) { return request('tools/call', { name: name, arguments: args || {} }); },
    sendMessage: function (text) { return request('ui/message', { role: 'user', content: [{ type: 'text', text: text }] }); },
    updateModelContext: function (text, data) { return request('ui/update-model-context', { content: [{ type: 'text', text: text }], structuredContent: data }); },
    openLink: function (url) { return request('ui/open-link', { url: url }); }
  };
})();

function h(tag, attrs) {
  var el = document.createElement(tag);
  if (attrs) for (var key in attrs) {
    var value = attrs[key];
    if (value === null || value === undefined || value === false) continue;
    if (key === 'class') el.className = value;
    else if (key === 'text') el.textContent = value;
    else if (key.indexOf('on') === 0) el.addEventListener(key.slice(2), value);
    else if (key === 'value' || key === 'checked' || key === 'disabled' || key === 'selected') el[key] = value;
    else el.setAttribute(key, value === true ? '' : value);
  }
  for (var i = 2; i < arguments.length; i++) add(el, arguments[i]);
  return el;
}

function add(el, child) {
  if (child === null || child === undefined || child === false) return;
  if (Array.isArray(child)) child.forEach(function (c) { add(el, c); });
  else el.appendChild(typeof child === 'object' ? child : document.createTextNode(String(child)));
}

function clear(el) { while (el.firstChild) el.removeChild(el.firstChild); }

function text(value) { return value === null || value === undefined ? '' : String(value); }

function icon(name) {
  var NS = 'http://www.w3.org/2000/svg';
  var svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('viewBox', '0 0 16 16');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('class', 'ic' + (name === 'running' ? ' rot' : ''));
  function part(tag, attrs) {
    var e = document.createElementNS(NS, tag);
    for (var k in attrs) e.setAttribute(k, attrs[k]);
    svg.appendChild(e);
  }
  var disc = { cx: 8, cy: 8, r: 7, fill: 'currentColor' };
  if (name === 'check') { part('circle', disc); part('path', { d: 'M4.8 8.2l2.1 2.1 4.3-4.4', 'class': 'mk' }); }
  else if (name === 'error') { part('circle', disc); part('path', { d: 'M5.6 5.6l4.8 4.8M10.4 5.6l-4.8 4.8', 'class': 'mk' }); }
  else if (name === 'info') { part('circle', disc); part('path', { d: 'M8 7.3v3.6M8 5v.1', 'class': 'mk' }); }
  else if (name === 'warning') { part('path', { d: 'M8 1.8l6.6 12H1.4z', fill: 'currentColor', stroke: 'currentColor', 'stroke-width': 1.5, 'stroke-linejoin': 'round' }); part('path', { d: 'M8 6.4v3.2M8 11.6v.1', 'class': 'mk' }); }
  else if (name === 'pending') part('circle', { cx: 8, cy: 8, r: 6.25, 'class': 'ln' });
  else if (name === 'running') part('path', { d: 'M8 1.75a6.25 6.25 0 1 1-6.25 6.25', 'class': 'ln' });
  else if (name === 'up') part('path', { d: 'M4 10l4-4 4 4', 'class': 'ln' });
  else if (name === 'down') part('path', { d: 'M4 6l4 4 4-4', 'class': 'ln' });
  else if (name === 'expand') part('path', { d: 'M9.5 2.5h4v4M6.5 13.5h-4v-4M13.5 2.5L9 7M2.5 13.5L7 9', 'class': 'ln' });
  else if (name === 'collapse') part('path', { d: 'M13 3L9.5 6.5M9.5 3.5v3h3M3 13l3.5-3.5M3.5 9.5h3v3', 'class': 'ln' });
  return svg;
}

// Fluent MessageBar. intent is error, success, warning, or info.
function msg(intent, title, body, action) {
  var icons = { error: 'error', success: 'check', warning: 'warning', info: 'info' };
  return h('div', { class: 'msg ' + intent, role: intent === 'error' ? 'alert' : 'status' },
    icon(icons[intent]),
    h('div', { class: 'body' }, title ? h('span', { class: 't', text: title + ' ' }) : null, body ? h('span', { text: body }) : null),
    action || null);
}

// Fluent button loading state: a spinner ahead of the label.
function setBusy(button, busy) {
  var spinner = button.querySelector('.spin');
  if (busy && !spinner) button.insertBefore(h('span', { class: 'spin', 'aria-hidden': 'true' }), button.firstChild);
  if (!busy && spinner) button.removeChild(spinner);
}

function showError(message) {
  var root = document.getElementById('root');
  clear(root);
  root.appendChild(h('div', { class: 'card' }, msg('error', 'Something went wrong.', message)));
  App.resize();
}

function start(name, sample, render, modes) {
  App.onToolResult(function (result) {
    if (result && result.isError) { showError('The card could not be loaded.'); return; }
    var data = result && result.structuredContent;
    if (data) render(data); else showError('The tool returned no data for this card.');
  });
  App.onToolCancelled(function () { showError('The request was cancelled.'); });
  if (App.isStandalone) { render(sample); return; }
  App.connect(name, modes).catch(function () { showError('This card needs a host that supports MCP Apps.'); });
}
";

    // ── Game styles ─────────────────────────────────────────────────────

    private const string GameCss = @"
.stats{display:flex;gap:16px;flex-wrap:wrap}
.stat{display:flex;flex-direction:column;min-width:56px}
.stat strong{font-size:20px;line-height:24px;font-weight:600;font-variant-numeric:tabular-nums}
.boardwrap{display:flex;justify-content:center;touch-action:none;border-radius:var(--r-m)}
.boardwrap:focus{outline:none}
.boardwrap:focus-visible :is(.board,.grid,.mines){outline:2px solid var(--focus);outline-offset:2px}
.board{display:block;height:auto;background:var(--bg3);border:1px solid var(--s1);border-radius:var(--r-m)}
.pad{display:grid;grid-template-columns:repeat(3,52px);gap:8px;justify-content:center}
.pad .btn{min-width:0;height:44px;padding:0;font-size:18px}
.sr{position:absolute;width:1px;height:1px;margin:-1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}
.stack>div.sr:empty{display:block}
";

    // ── Shared game kit ─────────────────────────────────────────────────
    //
    //    gameShell builds the card around a board: title, stats, board, optional controls,
    //    a status message, and at most two actions (a primary button and Expand). One game
    //    runs per page, so the keyboard, focus, and stop hooks live in a single Active object.

    private const string GameJs = @"
var DIRS = { up: { x: 0, y: -1 }, down: { x: 0, y: 1 }, left: { x: -1, y: 0 }, right: { x: 1, y: 0 } };
var KEYDIR = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right', w: 'up', s: 'down', a: 'left', d: 'right' };
var PAD4 = [
  { name: 'up', glyph: '\u25B2', label: 'Up', col: '2', row: 1 },
  { name: 'left', glyph: '\u25C0', label: 'Left', col: '1', row: 2 },
  { name: 'down', glyph: '\u25BC', label: 'Down', col: '2', row: 2 },
  { name: 'right', glyph: '\u25B6', label: 'Right', col: '3', row: 2 }
];
var Active = { key: null, away: null, stop: null, wrap: null };

document.addEventListener('keydown', function (e) {
  if (!Active.key || e.ctrlKey || e.metaKey || e.altKey) return;
  if (Active.key(e.key, e.target === Active.wrap)) e.preventDefault();
});
document.addEventListener('visibilitychange', function () { if (document.hidden && Active.away) Active.away(); });
window.addEventListener('blur', function () { if (Active.away) Active.away(); });

function rand(n) { return Math.floor(Math.random() * n); }

function clamp(value, min, max, fallback) {
  var n = Number(value);
  if (!isFinite(n)) n = fallback;
  return Math.max(min, Math.min(max, Math.round(n)));
}

function makePad(items, onPress) {
  var pad = h('div', { class: 'pad' });
  items.forEach(function (item) {
    pad.appendChild(h('button', {
      class: 'btn', type: 'button', 'aria-label': item.label, text: item.glyph,
      style: 'grid-column:' + item.col + ';grid-row:' + item.row,
      onclick: function () { onPress(item.name); }
    }));
  });
  return pad;
}

function gameShell(cfg) {
  if (Active.stop) Active.stop();
  Active.key = null; Active.away = null; Active.stop = null;

  var root = document.getElementById('root');
  clear(root);

  var statEls = {};
  var statsRow = h('div', { class: 'stats' });
  cfg.stats.forEach(function (s) {
    var value = h('strong', { text: '0' });
    statEls[s[0]] = value;
    statsRow.appendChild(h('div', { class: 'stat' }, h('span', { class: 'cap', text: s[1] }), value));
    if (cfg.mid && cfg.stats.indexOf(s) === 0) statsRow.appendChild(cfg.mid);
  });

  var wrap = h('div', { class: 'boardwrap', tabindex: '0', role: 'group', 'aria-label': cfg.title + ' board' }, cfg.board);
  var status = h('div', { class: 'status', 'aria-live': 'polite' });
  var live = h('div', { class: 'sr', 'aria-live': 'polite' });
  var primary = h('button', { class: 'btn primary', type: 'button' });
  var expand = h('button', { class: 'btn', type: 'button' });
  Active.wrap = wrap;

  function paintExpand(mode) {
    var full = mode === 'fullscreen';
    clear(expand);
    expand.appendChild(icon(full ? 'collapse' : 'expand'));
    expand.appendChild(h('span', { text: full ? 'Collapse' : 'Expand' }));
    expand.setAttribute('aria-label', full ? 'Return to the inline view' : 'Open full screen');
  }
  paintExpand(App.mode());
  App.onMode(paintExpand);
  expand.addEventListener('click', function () {
    App.requestDisplayMode(App.mode() === 'fullscreen' ? 'inline' : 'fullscreen').catch(function () { expand.style.display = 'none'; });
  });

  root.appendChild(h('div', { class: 'card stack' + (cfg.cls ? ' ' + cfg.cls : '') },
    h('div', { class: 'row between' }, h('h1', { text: cfg.title }), cfg.screen ? null : statsRow),
    cfg.intro ? h('p', { class: 'intro', text: cfg.intro }) : null,
    cfg.screen ? h('div', { class: 'screen' }, statsRow, wrap) : wrap,
    cfg.controls || null,
    status,
    live,
    h('div', { class: 'actions' }, primary, expand)));

  var kit = {
    wrap: wrap,
    stat: function (id, value) { statEls[id].textContent = String(value); },
    status: function (intent, title, body) {
      clear(status);
      if (intent) status.appendChild(msg(intent, title, body));
      App.resize();
    },
    label: function (text) { primary.textContent = text; },
    announce: function (text) { live.textContent = text; },
    focus: function () { try { wrap.focus({ preventScroll: true }); } catch (e) { wrap.focus(); } },
    onPrimary: function (fn) { primary.addEventListener('click', function () { fn(); kit.focus(); }); },
    onKey: function (fn) { Active.key = fn; },
    onAway: function (fn) { Active.away = fn; },
    onStop: function (fn) { Active.stop = fn; },
    onSwipe: function (fn) {
      var sx = 0, sy = 0, live = false;
      wrap.addEventListener('pointerdown', function (e) { sx = e.clientX; sy = e.clientY; live = true; });
      wrap.addEventListener('pointercancel', function () { live = false; });
      wrap.addEventListener('pointerup', function (e) {
        if (!live) return;
        live = false;
        var dx = e.clientX - sx, dy = e.clientY - sy;
        if (Math.max(Math.abs(dx), Math.abs(dy)) < 24) return;
        fn(Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up'));
      });
    },
    report: function (message, data) { App.updateModelContext(message, data || {}).catch(function () {}); }
  };

  wrap.addEventListener('pointerdown', function () { kit.focus(); });
  return kit;
}
";

    // ── Snake ───────────────────────────────────────────────────────────

    private const string SnakeCss = @"
:root{--bw:360px}
html[data-mode=fullscreen]{--bw:560px}
html[data-mode=fullscreen] body{padding:24px;background:#2b3034}
html[data-mode=fullscreen] .card.gsnake{max-width:640px;min-height:0;margin:0 auto;border-radius:36px;padding:24px}
.board{width:100%;max-width:var(--bw)}
.gsnake{--lcd:#9fcb12;--ink:#1b3300;position:relative;max-width:420px;margin:0 auto;padding:36px 24px 34px;color:#e4e6e3;background:radial-gradient(120% 60% at 50% 0%,rgba(255,255,255,.16),transparent 60%),linear-gradient(160deg,#6f7a84,#4e565d 55%,#3e454b);border:0;border-radius:48px 48px 76px 76px/44px 44px 96px 96px;box-shadow:inset 0 2px 0 rgba(255,255,255,.3),inset 0 -6px 12px rgba(0,0,0,.35),inset 6px 0 10px rgba(0,0,0,.18),inset -6px 0 10px rgba(0,0,0,.18),0 4px 10px rgba(0,0,0,.4)}
.gsnake::before{content:'';position:absolute;top:14px;left:50%;width:60px;height:7px;margin-left:-30px;border-radius:4px;background:#2a2f34;box-shadow:inset 0 2px 2px rgba(0,0,0,.7),0 1px 0 rgba(255,255,255,.25)}
.gsnake .row.between{gap:0}
.gsnake h1{flex:1 1 100%;margin-bottom:10px;font-size:13px;line-height:18px;font-weight:800;letter-spacing:.4em;text-align:center;text-transform:uppercase;color:#e4e8eb;text-shadow:0 -1px 0 rgba(0,0,0,.5)}
.gsnake .screen{padding:14px 16px;background:linear-gradient(180deg,#3b4147,#252a2f);border-radius:26px 26px 22px 22px;box-shadow:inset 0 2px 5px rgba(0,0,0,.7),0 0 0 2px #5a636b,0 1px 0 2px rgba(255,255,255,.18)}
.gsnake .stats{flex:1 1 100%;flex-wrap:nowrap;justify-content:space-between;gap:12px;padding:10px 12px 6px;background:var(--lcd);border-radius:6px 6px 0 0;box-shadow:inset 0 3px 5px rgba(0,0,0,.4)}
.gsnake .stat{flex-direction:row;align-items:baseline;gap:6px;min-width:0}
.gsnake .stat .cap{font-family:'Courier New',monospace;font-size:11px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:var(--ink)}
.gsnake .stat strong{font-family:'Courier New',monospace;font-size:22px;line-height:24px;font-weight:700;letter-spacing:.08em;color:var(--ink)}
.gsnake .screen>.boardwrap{padding:0 12px 12px;background:var(--lcd);border-radius:0 0 6px 6px;box-shadow:inset 0 -3px 5px rgba(0,0,0,.4)}
.gsnake .board{background:var(--lcd);border:2px solid var(--ink);border-radius:0;image-rendering:pixelated}
.gsnake .boardwrap:focus-visible .board{outline-color:var(--ink)}
.gsnake .btn{color:#2b3035;background:linear-gradient(#dde1e4,#a9b0b6);border:1px solid #3f464c;border-radius:20px;box-shadow:0 2px 0 #2c3237,inset 0 1px 0 rgba(255,255,255,.7)}
.gsnake .btn:hover:not(:disabled){background:linear-gradient(#ebeef0,#b9c0c5);border-color:#3f464c}
.gsnake .btn:active:not(:disabled){background:linear-gradient(#a9b0b6,#c4cacf);box-shadow:inset 0 2px 3px rgba(0,0,0,.35);transform:translateY(2px)}
.gsnake .actions{justify-content:center;gap:12px}
.gsnake .btn.primary{min-width:120px}
.gsnake .actions .btn{height:36px}
";

    private const string SnakeJs = @"
function playSnake(data) {
  var N = 20, G = 20;
  var interval = clamp(data.interval, 40, 400, 120);
  var canvas = h('canvas', { class: 'board', width: N * G, height: N * G, role: 'img', 'aria-label': 'Snake board' });
  var ctx = canvas.getContext('2d');
  var kit = gameShell({
    title: 'Snake', board: canvas, cls: 'gsnake', screen: true,
    stats: [['score', 'Score'], ['best', 'Best']],
    controls: makePad(PAD4, steer)
  });
  var INK = '#1b3300';
  var snake, dir, nextDir, food, score = 0, best = 0, state = 'ready', timer = null;

  function digits(n) {
    var s = String(n);
    while (s.length < 4) s = '0' + s;
    return s;
  }

  function place() {
    do { food = { x: rand(N), y: rand(N) }; }
    while (snake.some(function (s) { return s.x === food.x && s.y === food.y; }));
  }

  function reset() {
    snake = [{ x: 10, y: 10 }, { x: 9, y: 10 }, { x: 8, y: 10 }];
    dir = nextDir = DIRS.right;
    score = 0;
    place();
    kit.stat('score', digits(0));
    kit.stat('best', digits(best));
    draw();
  }

  function draw() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = INK;
    var cx = food.x * G + G / 2, cy = food.y * G + G / 2, r = G / 2 - 2;
    ctx.beginPath();
    ctx.moveTo(cx, cy - r); ctx.lineTo(cx + r, cy); ctx.lineTo(cx, cy + r); ctx.lineTo(cx - r, cy);
    ctx.closePath();
    ctx.fill();
    snake.forEach(function (s) {
      ctx.fillRect(s.x * G + 1, s.y * G + 1, G - 2, G - 2);
    });
    if (state === 'paused' || state === 'over') {
      ctx.fillStyle = 'rgba(159, 203, 18, 0.75)';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.fillStyle = INK;
      ctx.strokeStyle = INK;
      ctx.lineWidth = 3;
      ctx.strokeRect(60, 140, canvas.width - 120, 120);
      ctx.font = 'bold 30px ""Courier New"", monospace';
      ctx.textAlign = 'center';
      ctx.fillText(state === 'paused' ? 'PAUSED' : 'GAME OVER', canvas.width / 2, state === 'paused' ? 212 : 188);
      if (state === 'over') {
        ctx.font = 'bold 20px ""Courier New"", monospace';
        ctx.fillText('SCORE ' + digits(score), canvas.width / 2, 232);
      }
    }
  }

  function run() { clearInterval(timer); timer = setInterval(tick, interval); }

  function begin() {
    reset();
    state = 'running';
    kit.status(null);
    kit.label('Pause');
    run();
  }

  function pause() {
    if (state !== 'running') return;
    clearInterval(timer);
    state = 'paused';
    kit.label('Resume');
    draw();
  }

  function resume() {
    state = 'running';
    kit.status(null);
    kit.label('Pause');
    run();
  }

  function act() {
    if (state === 'running') pause();
    else if (state === 'paused') resume();
    else begin();
  }

  function over() {
    clearInterval(timer);
    state = 'over';
    if (score > best) { best = score; kit.stat('best', digits(best)); }
    kit.label('Restart');
    kit.announce('Game over. You scored ' + score + '.');
    kit.report('The user finished a game of Snake with a score of ' + score + '.',     { game: 'snake', score: score });
    draw();
  }

  function tick() {
    dir = nextDir;
    var head = { x: snake[0].x + dir.x, y: snake[0].y + dir.y };
    var eats = head.x === food.x && head.y === food.y;
    var body = eats ? snake : snake.slice(0, -1);
    if (head.x < 0 || head.x >= N || head.y < 0 || head.y >= N ||
        body.some(function (s) { return s.x === head.x && s.y === head.y; })) { over(); return; }
    snake.unshift(head);
    if (eats) { score++; kit.stat('score', digits(score)); place(); } else snake.pop();
    draw();
  }

  function steer(name) {
    var v = DIRS[name];
    if (state === 'ready') {
      begin();
      if (!(v.x === -dir.x && v.y === -dir.y)) dir = nextDir = v;
      return;
    }
    if (state !== 'running') return;
    if (v.x === -dir.x && v.y === -dir.y) return;
    nextDir = v;
  }

  kit.onPrimary(act);
  kit.onSwipe(steer);
  kit.onAway(pause);
  kit.onStop(function () { clearInterval(timer); });
  kit.onKey(function (key, onBoard) {
    var name = KEYDIR[key];
    if (name) { steer(name); return true; }
    if (onBoard && (key === ' ' || key === 'Enter')) { act(); return true; }
    return false;
  });

  kit.label('Start');
  reset();
}

start('Snake', { interval: 120 }, playSnake, ['inline', 'fullscreen']);
";

    // ── 2048 ────────────────────────────────────────────────────────────

    private const string Game2048Css = @"
:root{--bw:310px}
html[data-mode=fullscreen]{--bw:min(600px,calc(100vh - 250px))}
.g2048{position:relative;max-width:400px;margin:8px auto;background:#faf8ee;border:8px solid #1d1d1f;border-radius:30px;box-shadow:0 0 0 2px #5a5a5e,0 4px 12px rgba(0,0,0,.35);color:#776e65;padding:14px 14px 14px}
.g2048::before{content:'';position:absolute;top:-6px;left:50%;width:44px;height:3px;margin-left:-22px;border-radius:2px;background:#4a4a4e}
.g2048>*+*{margin-top:8px}
.g2048 .actions{margin-top:8px}
.g2048 .pad{grid-template-columns:repeat(4,44px);gap:8px}
.g2048 .pad .btn{height:34px;font-size:15px}
html[data-mode=fullscreen] body{padding:24px;background:#2b3034}
html[data-mode=fullscreen] .card.g2048{max-width:760px;min-height:0;margin:0 auto;padding:20px;border:8px solid #1d1d1f;border-radius:30px}
.g2048 .row.between{align-items:stretch;flex-wrap:nowrap;gap:12px}
.g2048 h1{display:flex;align-items:center;justify-content:center;flex:none;min-width:0;padding:8px 12px;font-size:28px;line-height:1;font-weight:800;color:#ffffff;background:#ecc501;border-radius:6px}
.g2048 .stats{flex:1;min-width:0;justify-content:flex-end;gap:8px;flex-wrap:nowrap}
.g2048 .stat{flex:1 1 0;min-width:0;max-width:84px;align-items:center;justify-content:center;padding:6px 6px;background:#bbada0;border-radius:6px}
.g2048 .stat .cap{font-size:12px;font-weight:700;letter-spacing:.04em;text-transform:uppercase;color:#e9e1d7}
.g2048 .stat strong{font-size:22px;color:#ffffff}
.g2048 .intro{margin:0;font-size:15px;font-weight:700;color:#776e65}
.g2048 .btn{color:#ffffff;background:#ec995a;border-color:transparent;border-radius:6px;font-weight:700;letter-spacing:.03em;text-transform:uppercase}
.g2048 .btn:hover:not(:disabled){background:#e88d48;border-color:transparent}
.g2048 .btn:active:not(:disabled){background:#d98040}
.g2048 .boardwrap:focus-visible .grid{outline-color:#ec995a}
.grid{position:relative;container-type:inline-size;display:grid;grid-template-columns:repeat(var(--n),1fr);gap:calc(var(--bw) / 28);width:100%;max-width:var(--bw);padding:calc(var(--bw) / 28);box-sizing:border-box;background:#bbada0;border-radius:6px}
.tile{aspect-ratio:1;display:flex;align-items:center;justify-content:center;border-radius:4px;font-weight:800;font-size:20px;font-size:calc(100cqw / var(--n) / 2.6);background:#ccc0b3;color:transparent}
.tile.d3{font-size:calc(100cqw / var(--n) / 3.2)}
.ovl{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px;background:rgba(250,248,238,.78);border-radius:6px}
.ovl p{margin:0;font-size:calc(100cqw / 8);line-height:1.1;font-weight:800;color:#776e65}
.ovl.win{background:rgba(237,194,46,.55)}
.ovl.win p{color:#ffffff}
.tile.d4{font-size:calc(100cqw / var(--n) / 4)}
.tile.d5{font-size:calc(100cqw / var(--n) / 5)}
.t2{background:#efe5da;color:#776e65}
.t4{background:#ece0c9;color:#776e65}
.t8{background:#f1b179;color:#ffffff}
.t16{background:#f59563;color:#ffffff}
.t32{background:#f67c5f;color:#ffffff}
.t64{background:#f65e3b;color:#ffffff}
.t128{background:#edcf72;color:#ffffff}
.t256{background:#edcc61;color:#ffffff}
.t512{background:#edc850;color:#ffffff}
.t1024{background:#edc53f;color:#ffffff}
.t2048{background:#edc22e;color:#ffffff}
.tbig{background:#3c3a32;color:#ffffff}
";

    private const string Game2048Js = @"
function play2048(data) {
  var N = clamp(data.gridSize, 3, 6, 4);
  var board = h('div', { class: 'grid', style: '--n:' + N, role: 'img', 'aria-label': '2048 board' });
  var kit = gameShell({
    title: '2048', board: board, cls: 'g2048',
    intro: 'Join the numbers and get to the 2048 tile!',
    stats: [['score', 'Score'], ['best', 'Best']],
    controls: makePad([
      { name: 'left', glyph: '\u25C0', label: 'Left', col: '1', row: 1 },
      { name: 'up', glyph: '\u25B2', label: 'Up', col: '2', row: 1 },
      { name: 'down', glyph: '\u25BC', label: 'Down', col: '3', row: 1 },
      { name: 'right', glyph: '\u25B6', label: 'Right', col: '4', row: 1 }
    ], move)
  });
  var grid = [], score = 0, best = 0, won = false, over = false;

  function add() {
    var empty = [];
    for (var i = 0; i < grid.length; i++) if (grid[i] === 0) empty.push(i);
    if (empty.length) grid[empty[rand(empty.length)]] = Math.random() < 0.9 ? 2 : 4;
  }

  function reset() {
    grid = [];
    for (var i = 0; i < N * N; i++) grid.push(0);
    score = 0; won = false; over = false;
    add(); add();
    render();
    kit.stat('score', 0);
    kit.status(null);
  }

  function showOverlay(kind) {
    var box = h('div', { class: 'ovl ' + kind }, h('p', { text: kind === 'win' ? 'You win!' : 'Game over!' }));
    var action = h('button', { class: 'btn', type: 'button', text: kind === 'win' ? 'Keep going' : 'Try again' });
    action.addEventListener('click', function () {
      if (kind === 'win') { box.remove(); kit.focus(); } else reset();
    });
    box.appendChild(action);
    board.appendChild(box);
  }

  function render() {
    clear(board);
    var rows = [];
    grid.forEach(function (v, i) {
      var cls = 'tile ' + (v ? 't' + (v > 2048 ? 'big' : v) : 't0') + ' d' + Math.min(5, String(v || 1).length);
      board.appendChild(h('div', { class: cls, text: v || '' }));
      if (i % N === 0) rows.push([]);
      rows[rows.length - 1].push(v || 'empty');
    });
    board.setAttribute('aria-label', '2048 board. ' + rows.map(function (r) { return r.join(', '); }).join('; '));
  }

  function slide(line) {
    var a = line.filter(function (x) { return x !== 0; });
    for (var i = 0; i < a.length - 1; i++) {
      if (a[i] === a[i + 1]) { a[i] *= 2; score += a[i]; a.splice(i + 1, 1); }
    }
    while (a.length < N) a.push(0);
    return a;
  }

  function canMove() {
    for (var i = 0; i < grid.length; i++) {
      if (grid[i] === 0) return true;
      if (i % N < N - 1 && grid[i] === grid[i + 1]) return true;
      if (i + N < grid.length && grid[i] === grid[i + N]) return true;
    }
    return false;
  }

  function move(name) {
    if (over) return;
    var old = grid.slice();
    var vertical = name === 'up' || name === 'down';
    var reverse = name === 'right' || name === 'down';
    for (var k = 0; k < N; k++) {
      var line = [];
      for (var j = 0; j < N; j++) line.push(vertical ? grid[j * N + k] : grid[k * N + j]);
      if (reverse) line.reverse();
      line = slide(line);
      if (reverse) line.reverse();
      for (var m = 0; m < N; m++) {
        if (vertical) grid[m * N + k] = line[m]; else grid[k * N + m] = line[m];
      }
    }
    var moved = grid.some(function (v, i) { return v !== old[i]; });
    if (!moved) return;
    add();
    render();
    kit.stat('score', score);
    if (score > best) { best = score; kit.stat('best', best); }
    if (!won && grid.indexOf(2048) !== -1) {
      won = true;
      showOverlay('win');
      kit.announce('You reached 2048!');
      kit.report('The user reached 2048 with a score of ' + score + '.', { game: '2048', score: score, won: true });
    } else if (!canMove()) {
      over = true;
      showOverlay('over');
      kit.announce('Game over. You scored ' + score + '.');
      kit.report('The user finished a game of 2048 with a score of ' + score + '.', { game: '2048', score: score, won: won });
    }
  }

  kit.label('New game');
  kit.onPrimary(reset);
  kit.onSwipe(move);
  kit.onKey(function (key) {
    var name = KEYDIR[key];
    if (!name) return false;
    move(name);
    return true;
  });
  reset();
}

start('2048', { gridSize: 4 }, play2048, ['inline', 'fullscreen']);
";

    // ── Minesweeper ─────────────────────────────────────────────────────

    private const string MinesCss = @"
.boardwrap{touch-action:pan-x pan-y;overflow-x:auto;justify-content:flex-start}
.boardwrap:focus-visible .mines{outline-offset:-2px}
html[data-mode=fullscreen] body{padding:24px;background:#008080}
html[data-mode=fullscreen] .card.gmines{width:max-content;max-width:100%;min-height:0;margin:0 auto;padding:3px}
.gmines{--raised:inset -1px -1px #0a0a0a,inset 1px 1px #ffffff,inset -2px -2px #808080,inset 2px 2px #dfdfdf;--sunken:inset 1px 1px #0a0a0a,inset -1px -1px #ffffff,inset 2px 2px #808080,inset -2px -2px #dfdfdf;width:fit-content;min-width:300px;max-width:100%;background:#c0c0c0;color:#000000;border:0;border-radius:0;padding:3px;box-shadow:var(--raised)}
.gmines .row.between{flex-wrap:wrap;gap:0}
.gmines h1{display:flex;align-items:center;justify-content:space-between;flex:1 1 100%;min-height:20px;padding:2px 3px 2px 6px;font-size:14px;line-height:16px;font-weight:700;color:#ffffff;background:#000080}
.gmines h1::after{content:'';flex:none;width:50px;height:14px;background:url('data:image/svg+xml,%3Csvg%20xmlns=%22http://www.w3.org/2000/svg%22%20width=%2250%22%20height=%2214%22%20shape-rendering=%22crispEdges%22%3E%3Cg%20id=%22b%22%3E%3Crect%20width=%2216%22%20height=%2214%22%20fill=%22%230a0a0a%22/%3E%3Crect%20width=%2215%22%20height=%2213%22%20fill=%22%23fff%22/%3E%3Crect%20x=%221%22%20y=%221%22%20width=%2214%22%20height=%2212%22%20fill=%22%23808080%22/%3E%3Crect%20x=%221%22%20y=%221%22%20width=%2213%22%20height=%2211%22%20fill=%22%23dfdfdf%22/%3E%3Crect%20x=%222%22%20y=%222%22%20width=%2212%22%20height=%2210%22%20fill=%22%23c0c0c0%22/%3E%3C/g%3E%3Cuse%20href=%22%23b%22%20x=%2216%22/%3E%3Cuse%20href=%22%23b%22%20x=%2234%22/%3E%3Crect%20x=%224%22%20y=%229%22%20width=%226%22%20height=%222%22/%3E%3Crect%20x=%2220%22%20y=%223%22%20width=%229%22%20height=%222%22/%3E%3Crect%20x=%2220%22%20y=%225%22%20width=%221%22%20height=%225%22/%3E%3Crect%20x=%2228%22%20y=%225%22%20width=%221%22%20height=%225%22/%3E%3Crect%20x=%2220%22%20y=%229%22%20width=%229%22%20height=%221%22/%3E%3Cpath%20d=%22M38%204L45%2010M45%204L38%2010%22%20stroke=%22%23000%22%20stroke-width=%221.6%22%20shape-rendering=%22auto%22/%3E%3C/svg%3E') no-repeat}
.gmines .stats{flex:1 1 100%;flex-wrap:nowrap;align-items:center;justify-content:space-between;gap:8px;margin:6px 3px 0;padding:5px 6px;box-shadow:var(--sunken)}
.gmines .stat{position:relative;min-width:0;padding:1px 2px;background:#000000;box-shadow:inset 1px 1px #808080,inset -1px -1px #ffffff}
.gmines .stat .cap{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
.gmines .stat strong{font-family:'Courier New',monospace;font-size:26px;line-height:30px;font-weight:700;letter-spacing:2px;color:#ff0000}
.gmines .face{width:36px;min-width:0;height:36px;padding:0;font-size:22px;line-height:1;background:#c0c0c0;border:0;border-radius:0;box-shadow:var(--raised);cursor:pointer}
.gmines .face:active{box-shadow:var(--sunken)}
.gmines>:not(:first-child){margin-left:6px;margin-right:6px}
.gmines>.boardwrap{margin-top:6px}
.gmines .cap{color:#000000}
.gmines .btn{color:#000000;background:#c0c0c0;border:0;border-radius:0;font-weight:400;box-shadow:var(--raised)}
.gmines .btn:hover:not(:disabled){background:#c0c0c0;border-color:transparent}
.gmines .btn:active:not(:disabled),.gmines .btn[aria-pressed=true]{background:#c0c0c0;box-shadow:var(--sunken)}
.gmines .actions{margin-bottom:6px}
.mines{display:grid;grid-template-columns:repeat(var(--w),var(--cs));gap:0;margin:0 auto;width:max-content;background:#c0c0c0;border:3px solid;border-color:#808080 #ffffff #ffffff #808080}
.cell{width:var(--cs);height:var(--cs);padding:0;border:0;border-radius:0;font:inherit;font-size:16px;font-weight:700;line-height:1;background:#c0c0c0;color:#000000;box-shadow:inset 2px 2px #ffffff,inset -2px -2px #808080;cursor:pointer}
.cell:focus-visible{outline:2px solid var(--focus);outline-offset:-4px}
.cell.open{box-shadow:inset 1px 1px #808080;cursor:default}
.cell.mine{background:#c0c0c0}
.cell.boom{background:#ff0000}
.cell.wrong{text-decoration:line-through;opacity:.7}
.c1{color:#0000ff}
.c2{color:#008000}
.c3{color:#ff0000}
.c4{color:#000080}
.c5{color:#800000}
.c6{color:#008080}
.c7{color:#000000}
.c8{color:#808080}
";

    private const string MinesJs = @"
function playMines(data) {
  var BOMB = '\uD83D\uDCA3', FLAG = '\uD83D\uDEA9';
  var SMILE = '\uD83D\uDE42', DEAD = '\uD83D\uDE35', COOL = '\uD83D\uDE0E', OOH = '\uD83D\uDE2E';
  var W = clamp(data.width, 5, 30, 9), H = clamp(data.height, 5, 30, 9), total = W * H;
  var M = clamp(data.mines, 1, total - 9, 10);
  var difficulty = data.difficulty || 'easy';
  var mine = [], count = [], open = [], flag = [], cells = [];
  var state = 'ready', flagMode = false, hit = -1, flags = 0, opened = 0;

  var board = h('div', { class: 'mines', style: '--w:' + W + ';--cs:' + (W > 20 ? 22 : 28) + 'px' });
  var flagBtn = h('button', { class: 'btn sm', type: 'button', 'aria-pressed': 'false', text: 'Flag mode: off' });
  var face = h('button', { class: 'face', type: 'button', 'aria-label': 'New game', text: SMILE });
  var timer = null, seconds = 0;
  var kit = gameShell({
    title: 'Minesweeper', board: board, cls: 'gmines', mid: face,
    stats: [['mines', 'Mines left'], ['time', 'Seconds']],
    controls: h('div', { class: 'row' }, flagBtn)
  });

  function led(n) {
    var s = String(Math.min(999, Math.abs(n)));
    while (s.length < 3) s = '0' + s;
    return n < 0 ? '-' + s.slice(1) : s;
  }

  function stopClock() { clearInterval(timer); timer = null; }

  function startClock() {
    stopClock();
    timer = setInterval(function () { seconds++; kit.stat('time', led(seconds)); }, 1000);
  }

  Active.stop = stopClock;

  function neighbors(i) {
    var r = Math.floor(i / W), c = i % W, out = [];
    for (var dr = -1; dr <= 1; dr++) for (var dc = -1; dc <= 1; dc++) {
      if (!dr && !dc) continue;
      var nr = r + dr, nc = c + dc;
      if (nr >= 0 && nr < H && nc >= 0 && nc < W) out.push(nr * W + nc);
    }
    return out;
  }

  function paint(i) {
    var cell = cells[i], cls = 'cell', label = 'Hidden', face = '';
    if (open[i]) {
      cls += ' open';
      if (mine[i]) { cls += i === hit ? ' mine boom' : ' mine'; face = BOMB; label = 'Mine'; }
      else if (count[i]) { cls += ' c' + count[i]; face = String(count[i]); label = count[i] + ' nearby'; }
      else label = 'Empty';
    } else if (flag[i]) {
      cls += ' flag'; face = FLAG; label = 'Flagged';
      if (state === 'lost' && !mine[i]) cls += ' wrong';
    }
    cell.className = cls;
    cell.textContent = face;
    cell.setAttribute('aria-label', 'Row ' + (Math.floor(i / W) + 1) + ', column ' + (i % W + 1) + ', ' + label);
  }

  function paintAll() { for (var i = 0; i < total; i++) paint(i); }

  function placeMines(safe) {
    var banned = {}, around = neighbors(safe);
    banned[safe] = true;
    if (total - 1 - around.length >= M) around.forEach(function (n) { banned[n] = true; });
    var placed = 0;
    while (placed < M) {
      var i = rand(total);
      if (banned[i] || mine[i]) continue;
      mine[i] = true;
      placed++;
    }
    for (var j = 0; j < total; j++) {
      if (!mine[j]) count[j] = neighbors(j).filter(function (n) { return mine[n]; }).length;
    }
  }

  function toggleFlag(i) {
    if (state === 'won' || state === 'lost' || open[i]) return;
    flag[i] = !flag[i];
    flags += flag[i] ? 1 : -1;
    kit.stat('mines', led(M - flags));
    paint(i);
  }

  function lose(i) {
    state = 'lost';
    stopClock();
    face.textContent = DEAD;
    hit = i;
    for (var j = 0; j < total; j++) if (mine[j]) open[j] = true;
    paintAll();
    kit.announce('You hit a mine. Game over.');
    kit.report('The user lost a game of Minesweeper on ' + difficulty + '.', { game: 'minesweeper', difficulty: difficulty, won: false });
  }

  function win() {
    state = 'won';
    stopClock();
    face.textContent = COOL;
    for (var j = 0; j < total; j++) if (mine[j]) flag[j] = true;
    kit.stat('mines', led(0));
    paintAll();
    kit.announce('You win! You cleared the board.');
    kit.report('The user won a game of Minesweeper on ' + difficulty + '.', { game: 'minesweeper', difficulty: difficulty, won: true });
  }

  function reveal(i) {
    if (state === 'won' || state === 'lost' || open[i] || flag[i]) return;
    if (state === 'ready') { placeMines(i); state = 'playing'; kit.status(null); startClock(); }
    if (mine[i]) { lose(i); return; }
    var stack = [i];
    while (stack.length) {
      var n = stack.pop();
      if (open[n] || flag[n]) continue;
      open[n] = true;
      opened++;
      paint(n);
      if (count[n] === 0) neighbors(n).forEach(function (m) { if (!open[m]) stack.push(m); });
    }
    if (opened === total - M) win();
  }

  function press(i) { if (flagMode) toggleFlag(i); else reveal(i); }

  function reset() {
    clear(board);
    cells = []; mine = []; count = []; open = []; flag = [];
    state = 'ready'; hit = -1; flags = 0; opened = 0; seconds = 0;
    stopClock();
    face.textContent = SMILE;
    for (var i = 0; i < total; i++) {
      mine.push(false); count.push(0); open.push(false); flag.push(false);
      var cell = h('button', { class: 'cell', type: 'button' });
      cell.addEventListener('click', (function (idx) { return function () { press(idx); }; })(i));
      cell.addEventListener('contextmenu', (function (idx) { return function (e) { e.preventDefault(); toggleFlag(idx); }; })(i));
      cells.push(cell);
      board.appendChild(cell);
      paint(i);
    }
    kit.stat('mines', led(M));
    kit.stat('time', led(0));
    kit.status(null);
  }

  face.addEventListener('click', function () { reset(); kit.focus(); });
  board.addEventListener('pointerdown', function (e) {
    if (e.button === 0 && !flagMode && (state === 'ready' || state === 'playing')) face.textContent = OOH;
  });
  document.addEventListener('pointerup', function () {
    if (face.textContent === OOH) face.textContent = SMILE;
  });

  flagBtn.addEventListener('click', function () {
    flagMode = !flagMode;
    flagBtn.setAttribute('aria-pressed', String(flagMode));
    flagBtn.textContent = 'Flag mode: ' + (flagMode ? 'on' : 'off');
    flagBtn.className = 'btn sm' + (flagMode ? ' primary' : '');
  });

  kit.label('New game');
  kit.onPrimary(reset);
  reset();
}

start('Minesweeper', { difficulty: 'easy', width: 9, height: 9, mines: 10 }, playMines, ['inline', 'fullscreen']);
";

    // ── Tetris ──────────────────────────────────────────────────────────

    private const string TetrisCss = @"
:root{--bw:200px}
html[data-mode=fullscreen]{--bw:300px}
html[data-mode=fullscreen] body{padding:24px;background:#0d0d2b}
html[data-mode=fullscreen] .card.gtetris{max-width:640px;min-height:0;margin:0 auto;padding:20px;border:6px solid transparent;border-radius:16px}
.gtetris{--gold:linear-gradient(135deg,#fff2b8,#c89a2e 35%,#fff2b8 55%,#8a6410);--silver:linear-gradient(135deg,#f2f2ff,#7b7bbd 50%,#f2f2ff);width:fit-content;max-width:100%;margin:10px auto;padding:16px;font-family:'Trebuchet MS','Segoe UI',sans-serif;font-weight:700;color:#ffffff;background:radial-gradient(1.5px 1.5px at 20% 15%,#fff 50%,transparent) 0 0/90px 70px,radial-gradient(1px 1px at 70% 60%,#cfd2ff 50%,transparent) 0 0/60px 80px,linear-gradient(180deg,#34227f,#14134d 55%,#221a66) padding-box,var(--gold) border-box;border:6px solid transparent;border-radius:16px;box-shadow:0 4px 14px rgba(0,0,0,.5)}
.gtetris .row.between{flex-wrap:wrap;gap:12px;align-items:center}
.gtetris h1{flex:1 1 100%;font-size:26px;line-height:30px;font-weight:800;letter-spacing:.2em;text-align:center;text-transform:uppercase;color:#ffd34d;text-shadow:0 2px 0 #3a1c00,0 0 10px rgba(255,211,77,.45)}
@supports ((-webkit-background-clip:text) or (background-clip:text)){.gtetris h1{color:transparent;text-shadow:none;background:linear-gradient(#ffffff,#ffd34d 55%,#d98c00);-webkit-background-clip:text;background-clip:text;filter:drop-shadow(0 2px 0 #3a1c00)}}
.gtetris .stats{flex:1 1 100%;flex-wrap:nowrap;justify-content:space-between;gap:8px}
.gtetris .stat{flex:1;min-width:0;padding:4px 8px;text-align:center;background:linear-gradient(180deg,#1d1d70,#060624) padding-box,var(--silver) border-box;border:3px solid transparent;border-radius:10px}
.gtetris .cap,.gtetris .stat .cap{font-size:11px;letter-spacing:.12em;text-transform:uppercase;color:#c9c6ff}
.gtetris .stat strong{font-size:20px;line-height:24px;color:#ffffff;text-shadow:0 0 6px rgba(160,160,255,.8)}
.tetris{display:flex;flex-wrap:wrap;gap:20px;align-items:flex-start;justify-content:center}
.tetris .board{width:var(--bw);flex:none;background:linear-gradient(rgba(255,255,255,.07) 1px,transparent 1px) 0 0/100% 5% padding-box,linear-gradient(90deg,rgba(255,255,255,.07) 1px,transparent 1px) 0 0/10% 100% padding-box,linear-gradient(#000,#000) padding-box,var(--silver) border-box;border:4px solid transparent;border-radius:8px}
.side{display:flex;flex-direction:column;gap:8px}
.side .pad{grid-template-columns:repeat(3,40px);gap:6px;margin-top:12px;justify-content:start}
.side .pad .btn{height:40px;font-size:16px;border-radius:50%}
.next{width:80px;height:80px;background:linear-gradient(180deg,#1d1d70,#060624) padding-box,var(--silver) border-box;border:3px solid transparent;border-radius:10px}
.gtetris .btn{color:#2a1b6b;background:linear-gradient(#f6f3ff,#b9b2e6);border:2px solid #5a4fa0;border-radius:999px;font-weight:800;letter-spacing:.03em;box-shadow:0 2px 0 #3a2f7a,inset 0 1px 0 #ffffff}
.gtetris .btn.primary{color:#ffffff;background:linear-gradient(#a98af0,#5a3aa8);border-color:#2f1d73}
.gtetris .btn:hover:not(:disabled){background:linear-gradient(#ffffff,#cbc5f2);border-color:#5a4fa0}
.gtetris .btn.primary:hover:not(:disabled){background:linear-gradient(#b99cf6,#6a48bb);border-color:#2f1d73}
.gtetris .btn:active:not(:disabled){background:linear-gradient(#b9b2e6,#9a92d6);box-shadow:inset 0 2px 3px rgba(0,0,0,.35);transform:translateY(2px)}
.gtetris .btn.primary:active:not(:disabled){background:linear-gradient(#5a3aa8,#7a58c8)}
.gtetris .msg{color:#ffffff;font-weight:400;background:rgba(6,6,36,.8);border:2px solid #7b7bbd;border-radius:10px}
.gtetris .boardwrap:focus-visible .board{outline-color:#ffd34d}
";

    private const string TetrisJs = @"
function playTetris(data) {
  var COLS = 10, ROWS = 20, SZ = 24;
  var startLevel = clamp(data.level, 1, 20, 1);
  var PIECES = [
    [[1, 1, 1, 1]],
    [[1, 1], [1, 1]],
    [[0, 1, 0], [1, 1, 1]],
    [[1, 0, 0], [1, 1, 1]],
    [[0, 0, 1], [1, 1, 1]],
    [[1, 1, 0], [0, 1, 1]],
    [[0, 1, 1], [1, 1, 0]]
  ];
  // Tetris Guideline colors in piece order: I, O, T, J, L, S, Z
  var COLORS = ['#00f0f0', '#f0f000', '#a000f0', '#0000f0', '#f0a000', '#00f000', '#f00000'];

  function shade(hex, mode) {
    var n = parseInt(hex.slice(1), 16), out = '#';
    [n >> 16 & 255, n >> 8 & 255, n & 255].forEach(function (v) {
      var s = mode === 'light' ? Math.min(255, v + 153) : mode === 'dark' ? Math.round(v * 0.5) : Math.round(v * 0.9);
      out += (s < 16 ? '0' : '') + s.toString(16);
    });
    return out;
  }

  function face(g, pts, color) {
    g.fillStyle = color;
    g.beginPath();
    g.moveTo(pts[0][0], pts[0][1]);
    for (var i = 1; i < pts.length; i++) g.lineTo(pts[i][0], pts[i][1]);
    g.closePath();
    g.fill();
  }

  function bevel(g, px, py, size, color) {
    var b = Math.max(2, Math.round(size / 8)), x2 = px + size, y2 = py + size;
    g.fillStyle = color;
    g.fillRect(px, py, size, size);
    face(g, [[px, py], [x2, py], [x2 - b, py + b], [px + b, py + b]], shade(color, 'light'));
    face(g, [[px, y2], [x2, y2], [x2 - b, y2 - b], [px + b, y2 - b]], shade(color, 'dark'));
    face(g, [[px, py], [px + b, py + b], [px + b, y2 - b], [px, y2]], shade(color, 'side'));
    face(g, [[x2, py], [x2, y2], [x2 - b, y2 - b], [x2 - b, py + b]], shade(color, 'side'));
  }

  var canvas = h('canvas', { class: 'board', width: COLS * SZ, height: ROWS * SZ, role: 'img', 'aria-label': 'Tetris board' });
  var nextCanvas = h('canvas', { class: 'next', width: 80, height: 80, role: 'img', 'aria-label': 'Next piece' });
  var ctx = canvas.getContext('2d'), nctx = nextCanvas.getContext('2d');
  var pad = makePad([
    { name: 'rotate', glyph: '\u21BB', label: 'Rotate', col: '2', row: 1 },
    { name: 'left', glyph: '\u25C0', label: 'Left', col: '1', row: 2 },
    { name: 'down', glyph: '\u25BC', label: 'Down', col: '2', row: 2 },
    { name: 'right', glyph: '\u25B6', label: 'Right', col: '3', row: 2 },
    { name: 'drop', glyph: 'Drop', label: 'Hard drop', col: '1 / 4', row: 3 }
  ], control);
  var layout = h('div', { class: 'tetris' }, canvas, h('div', { class: 'side' }, h('span', { class: 'cap', text: 'Next' }), nextCanvas, pad));
  var kit = gameShell({
    title: 'Tetris', board: layout, cls: 'gtetris',
    stats: [['score', 'Score'], ['lines', 'Lines'], ['level', 'Level']]
  });

  var grid = [], cur = null, curX = 0, curY = 0, curColor = '', nextShape = null, nextColor = '';
  var score = 0, lines = 0, level = startLevel, state = 'ready', timer = null;

  function newRow() { var row = []; for (var c = 0; c < COLS; c++) row.push(0); return row; }
  function emptyGrid() { var g = []; for (var r = 0; r < ROWS; r++) g.push(newRow()); return g; }

  function pick() {
    var i = rand(PIECES.length);
    nextShape = PIECES[i];
    nextColor = COLORS[i];
  }

  function collides(shape, px, py) {
    for (var r = 0; r < shape.length; r++) for (var c = 0; c < shape[r].length; c++) {
      if (!shape[r][c]) continue;
      var nr = py + r, nc = px + c;
      if (nc < 0 || nc >= COLS || nr >= ROWS) return true;
      if (nr >= 0 && grid[nr][nc]) return true;
    }
    return false;
  }

  function spawn() {
    cur = nextShape; curColor = nextColor;
    pick();
    curX = Math.floor((COLS - cur[0].length) / 2);
    curY = 0;
    drawNext();
    if (collides(cur, curX, curY)) { over(); return false; }
    return true;
  }

  function block(g, x, y, color, size) {
    bevel(g, x * size, y * size, size, color);
  }

  function draw() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    for (var r = 0; r < ROWS; r++) for (var c = 0; c < COLS; c++) {
      if (grid[r][c]) block(ctx, c, r, grid[r][c], SZ);
    }
    if (cur && (state === 'running' || state === 'paused')) {
      var gy = curY;
      while (!collides(cur, curX, gy + 1)) gy++;
      ctx.globalAlpha = 0.25;
      for (var r1 = 0; r1 < cur.length; r1++) for (var c1 = 0; c1 < cur[r1].length; c1++) {
        if (cur[r1][c1]) block(ctx, curX + c1, gy + r1, curColor, SZ);
      }
      ctx.globalAlpha = 1;
      for (var r2 = 0; r2 < cur.length; r2++) for (var c2 = 0; c2 < cur[r2].length; c2++) {
        if (cur[r2][c2]) block(ctx, curX + c2, curY + r2, curColor, SZ);
      }
    }
    if (state === 'over' || state === 'paused') {
      ctx.fillStyle = 'rgba(0, 0, 0, 0.55)';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.fillStyle = '#ffffff';
      ctx.font = 'bold 26px system-ui, sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText(state === 'over' ? 'GAME OVER' : 'PAUSED', canvas.width / 2, canvas.height / 2);
      if (state === 'over') {
        ctx.font = 'bold 16px system-ui, sans-serif';
        ctx.fillText('Score ' + score, canvas.width / 2, canvas.height / 2 + 28);
      }
    }
  }

  function drawNext() {
    nctx.clearRect(0, 0, 80, 80);
    var ox = Math.floor((80 - nextShape[0].length * 18) / 2), oy = Math.floor((80 - nextShape.length * 18) / 2);
    for (var r = 0; r < nextShape.length; r++) for (var c = 0; c < nextShape[r].length; c++) {
      if (nextShape[r][c]) bevel(nctx, ox + c * 18, oy + r * 18, 18, nextColor);
    }
  }

  function stats() {
    kit.stat('score', score);
    kit.stat('lines', lines);
    kit.stat('level', level);
  }

  function run() {
    clearInterval(timer);
    if (state === 'running') timer = setInterval(tick, Math.max(50, 500 - level * 40));
  }

  function sweep() {
    var cleared = 0;
    for (var r = ROWS - 1; r >= 0; r--) {
      if (grid[r].every(function (v) { return v !== 0; })) {
        grid.splice(r, 1);
        grid.unshift(newRow());
        cleared++;
        r++;
      }
    }
    if (!cleared) return;
    lines += cleared;
    score += [0, 100, 300, 500, 800][cleared] * level;
    level = startLevel + Math.floor(lines / 10);
    stats();
    run();
  }

  function lock() {
    for (var r = 0; r < cur.length; r++) for (var c = 0; c < cur[r].length; c++) {
      if (cur[r][c] && curY + r >= 0) grid[curY + r][curX + c] = curColor;
    }
    sweep();
    if (spawn()) draw();
  }

  function rotate() {
    var rotated = [];
    for (var c = 0; c < cur[0].length; c++) {
      var row = [];
      for (var r = cur.length - 1; r >= 0; r--) row.push(cur[r][c]);
      rotated.push(row);
    }
    var kicks = [0, -1, 1, -2, 2];
    for (var i = 0; i < kicks.length; i++) {
      if (!collides(rotated, curX + kicks[i], curY)) { cur = rotated; curX += kicks[i]; return; }
    }
  }

  function tick() {
    if (state !== 'running') return;
    if (!collides(cur, curX, curY + 1)) { curY++; draw(); } else lock();
  }

  function control(name) {
    if (state !== 'running') return;
    if (name === 'left' && !collides(cur, curX - 1, curY)) curX--;
    else if (name === 'right' && !collides(cur, curX + 1, curY)) curX++;
    else if (name === 'down' && !collides(cur, curX, curY + 1)) curY++;
    else if (name === 'rotate') rotate();
    else if (name === 'drop') {
      while (!collides(cur, curX, curY + 1)) curY++;
      lock();
      return;
    }
    draw();
  }

  function begin() {
    grid = emptyGrid();
    score = 0; lines = 0; level = startLevel;
    stats();
    pick();
    state = 'running';
    kit.status(null);
    kit.label('Pause');
    if (spawn()) { draw(); run(); }
  }

  function pause() {
    if (state !== 'running') return;
    clearInterval(timer);
    state = 'paused';
    kit.label('Resume');
    draw();
  }

  function resume() {
    state = 'running';
    kit.status(null);
    kit.label('Pause');
    run();
  }

  function act() {
    if (state === 'running') pause();
    else if (state === 'paused') resume();
    else begin();
  }

  function over() {
    clearInterval(timer);
    state = 'over';
    kit.label('Restart');
    kit.announce('Game over. You scored ' + score + ' and cleared ' + lines + (lines === 1 ? ' line.' : ' lines.'));
    kit.report('The user finished a game of Tetris with a score of ' + score + ', ' + lines + ' lines, level ' + level + '.', { game: 'tetris', score: score, lines: lines, level: level });
    draw();
  }

  kit.onPrimary(act);
  kit.onAway(pause);
  kit.onStop(function () { clearInterval(timer); });
  kit.onKey(function (key, onBoard) {
    var name = KEYDIR[key];
    if (name === 'up') name = 'rotate';
    if (name) { control(name); return state === 'running'; }
    if (key === ' ' && state === 'running') { control('drop'); return true; }
    if ((key === 'p' || key === 'P') && (state === 'running' || state === 'paused')) { act(); return true; }
    if (onBoard && (key === ' ' || key === 'Enter')) { act(); return true; }
    return false;
  });

  grid = emptyGrid();
  pick();
  drawNext();
  draw();
  stats();
  kit.label('Start');
}

start('Tetris', { level: 1 }, playTetris, ['inline', 'fullscreen']);
";
}
