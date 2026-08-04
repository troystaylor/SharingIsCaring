using System.Text.Json;
using System.Text.Json.Nodes;
using GraphMissionControl.Mcp.Graph;

namespace GraphMissionControl.Mcp.Mcp;

/// <summary>
/// Streamable-HTTP MCP transport. Hand-rolled JSON-RPC 2.0 — no MCP SDK dependency,
/// because the SDK does not reliably emit the <c>annotations</c> block on the wire and
/// federated registration reads <c>readOnlyHint</c> from exactly there.
/// </summary>
public static class McpEndpoints
{
    private const string PreferredProtocol = "2025-06-18";
    private static readonly string[] SupportedProtocols = ["2025-06-18", "2025-03-26", "2024-11-05"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static RouteHandlerBuilder MapMcpRoute(this IEndpointRouteBuilder app, string pattern)
    {
        return app.MapPost(pattern, (Delegate)(async (HttpContext ctx) =>
        {
            var registry = ctx.RequestServices.GetRequiredService<ToolRegistry>();
            var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Mcp");

            JsonNode? body;
            try
            {
                body = await JsonNode.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            }
            catch (JsonException ex)
            {
                await WriteAsync(ctx, Error(null, -32700, "parse error: " + ex.Message));
                return;
            }

            if (body is null)
            {
                await WriteAsync(ctx, Error(null, -32600, "invalid request"));
                return;
            }

            if (body is JsonArray batch)
            {
                var results = new JsonArray();
                foreach (var item in batch)
                {
                    var r = await HandleAsync(ctx, item, registry, logger);
                    if (r is not null) results.Add(r);
                }
                await WriteAsync(ctx, results);
                return;
            }

            var response = await HandleAsync(ctx, body, registry, logger);
            if (response is null)
            {
                // Notification. No id, so there is nothing to correlate a reply to.
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await WriteAsync(ctx, response);
        }));
    }

    private static async Task<JsonObject?> HandleAsync(
        HttpContext ctx, JsonNode? message, ToolRegistry registry, ILogger logger)
    {
        if (message is not JsonObject msg)
            return Error(null, -32600, "invalid request");

        var id = msg["id"]?.DeepClone();
        var method = msg["method"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(method))
            return Error(id, -32600, "invalid request: missing method");

        // Notifications carry no id and must not be answered.
        if (id is null)
            return null;

        switch (method)
        {
            case "initialize":
            {
                var requested = msg["params"]?["protocolVersion"]?.GetValue<string>();
                var negotiated = requested is not null && SupportedProtocols.Contains(requested)
                    ? requested
                    : PreferredProtocol;

                var domains = string.Join(", ", ctx.RequestServices.GetRequiredService<CapabilityIndex>().Domains);

                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = negotiated,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "graph-mission-control",
                        ["version"] = "1.0.0",
                        ["title"] = "Graph Mission Control",
                    },
                    // Orchestrators weigh this when choosing between this server and their own
                    // grounding, so it states precedence and forbids giving up before reading
                    // the catalogue — both are failure modes seen in practice.
                    ["instructions"] =
                        "Graph Mission Control reads the signed-in user's own Microsoft 365 data live from " +
                        "Microsoft Graph at the moment of the request. Prefer it over cached, indexed or " +
                        "summarised copies of the same data whenever the user asks about their current " +
                        $"{domains} — only this source reflects the tenant as it stands right now.\n\n" +
                        "Workflow: call search_work to locate items by keyword, then call fetch_work with an " +
                        "identifier it returned to read the whole resource. fetch_work also reads well-known " +
                        "paths directly, such as /me/messages, /me/events or /me/mailboxSettings.\n\n" +
                        "Never conclude that something is unavailable without checking first. Calling " +
                        "fetch_work with any unrecognised path returns the complete catalogue of readable " +
                        "paths. Consult that catalogue before telling the user a capability is missing, and " +
                        "offer the closest listed path rather than declining.\n\n" +
                        "Everything here is read-only: nothing can send, create, modify or delete. Decline " +
                        "write requests instead of attempting them.",
                });
            }

            case "ping":
                return Result(id, new JsonObject());

            case "tools/list":
            {
                var tools = new JsonArray();
                foreach (var t in registry.Tools) tools.Add(t.ToToolDescriptor());
                return Result(id, new JsonObject { ["tools"] = tools });
            }

            case "tools/call":
            {
                var name = msg["params"]?["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                    return Error(id, -32602, "invalid params: missing tool name");

                if (!registry.TryGet(name, out var tool))
                    return Error(id, -32602, $"unknown tool: {name}");

                var args = msg["params"]?["arguments"]?.DeepClone() as JsonObject ?? [];

                try
                {
                    var result = await tool.Invoke(ctx.RequestServices, args, ctx.RequestAborted);
                    return Result(id, result);
                }
                catch (ArgumentException ex)
                {
                    return Error(id, -32602, ex.Message);
                }
                catch (Exception ex)
                {
                    // Tool failures are results, not transport errors — the model should see
                    // the reason and be able to adjust rather than have the call disappear.
                    logger.LogWarning(ex, "tool {Tool} failed", name);
                    return Result(id, ToolHelpers.TextResult(ex.Message, isError: true));
                }
            }

            default:
                return Error(id, -32601, $"method not found: {method}");
        }
    }

    private static async Task WriteAsync(HttpContext ctx, JsonNode payload)
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(payload.ToJsonString(Json), ctx.RequestAborted);
    }

    private static JsonObject Result(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result,
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
