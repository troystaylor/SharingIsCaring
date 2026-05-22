using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Auth;
using SlackCoworkMcp.Slack;
using SlackCoworkMcp.Tools;

namespace SlackCoworkMcp.Endpoints;

/// <summary>
/// Minimal Model Context Protocol HTTP handler.
///
/// Implements the streamable-HTTP transport: a single POST endpoint that accepts
/// JSON-RPC 2.0 requests (initialize, tools/list, tools/call) and returns JSON
/// responses. SSE notifications are not required for stateless tool calls and
/// are omitted to keep the surface area small.
///
/// The same handler is mapped twice (one route per tool filter) so the server
/// can expose a full toolset to Cowork and a read-only subset to the M365
/// federated connector.
/// </summary>
public static class McpEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static IEndpointRouteBuilder MapMcpRoute(
        this IEndpointRouteBuilder app,
        string path,
        IReadOnlySet<string>? filter,
        bool requireAuthorization = false)
    {
        var endpoint = app.MapPost(path, async (HttpContext ctx) =>
        {
            var registry = ctx.RequestServices.GetRequiredService<ToolRegistry>();

            JsonNode? body;
            try
            {
                body = await JsonNode.ParseAsync(ctx.Request.Body);
            }
            catch (JsonException ex)
            {
                return WriteError(ctx, id: null, code: -32700, message: "parse error: " + ex.Message);
            }

            if (body is null)
            {
                return WriteError(ctx, id: null, code: -32600, message: "invalid request");
            }

            // Support batched calls
            if (body is JsonArray arr)
            {
                var results = new JsonArray();
                foreach (var item in arr)
                {
                    var r = await HandleAsync(ctx, item, registry, filter);
                    if (r is not null) results.Add(r);
                }
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(results.ToJsonString(Json));
                return Results.Empty;
            }

            var resp = await HandleAsync(ctx, body, registry, filter);
            if (resp is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return Results.Empty;
            }

            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(resp.ToJsonString(Json));
            return Results.Empty;
        });

        if (requireAuthorization)
        {
            endpoint.RequireAuthorization();
        }

        return app;
    }

    private static async Task<JsonObject?> HandleAsync(
        HttpContext ctx,
        JsonNode? message,
        ToolRegistry registry,
        IReadOnlySet<string>? filter)
    {
        if (message is not JsonObject msg)
        {
            return Err(null, -32600, "invalid request");
        }

        var id = msg["id"]?.DeepClone();
        var method = msg["method"]?.GetValue<string>();

        // Notifications (no id) — return null (no response)
        if (id is null && method is not null && method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return null;
        }

        if (string.IsNullOrEmpty(method))
        {
            return Err(id, -32600, "missing method");
        }

        try
        {
            return method switch
            {
                "initialize" => HandleInitialize(id, msg),
                "tools/list" => HandleToolsList(id, registry, filter),
                "tools/call" => await HandleToolsCall(ctx, id, msg, registry, filter),
                "ping" => Ok(id, new JsonObject()),
                _ => Err(id, -32601, $"method not found: {method}"),
            };
        }
        catch (UnauthorizedSlackException ex)
        {
            return Err(id, -32001, ex.Message);
        }
        catch (SlackApiException ex)
        {
            return Err(id, -32010, ex.Message, ex.ToData());
        }
        catch (Exception ex)
        {
            return Err(id, -32000, "internal error: " + ex.Message);
        }
    }

    private static JsonObject HandleInitialize(JsonNode? id, JsonObject msg)
    {
        var clientProto = msg["params"]?["protocolVersion"]?.GetValue<string>() ?? "2024-11-05";
        var result = new JsonObject
        {
            ["protocolVersion"] = clientProto,
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "slack-cowork-mcp",
                ["version"] = "1.0.0",
            },
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
            },
        };
        return Ok(id, result);
    }

    private static JsonObject HandleToolsList(JsonNode? id, ToolRegistry registry, IReadOnlySet<string>? filter)
    {
        var list = new JsonArray();
        foreach (var t in registry.Tools)
        {
            if (filter is not null && !filter.Contains(t.Name)) continue;
            list.Add(t.ToToolDescriptor());
        }
        return Ok(id, new JsonObject { ["tools"] = list });
    }

    private static async Task<JsonObject> HandleToolsCall(
        HttpContext ctx,
        JsonNode? id,
        JsonObject msg,
        ToolRegistry registry,
        IReadOnlySet<string>? filter)
    {
        var name = msg["params"]?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
        {
            return Err(id, -32602, "missing tool name");
        }

        if (filter is not null && !filter.Contains(name))
        {
            return Err(id, -32601, $"tool not exposed on this route: {name}");
        }

        if (!registry.TryGet(name, out var tool))
        {
            return Err(id, -32601, $"unknown tool: {name}");
        }

        var args = msg["params"]?["arguments"] as JsonObject ?? new JsonObject();
        var result = await tool!.InvokeAsync(ctx.RequestServices, args, ctx.RequestAborted);
        return Ok(id, result);
    }

    private static JsonObject Ok(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result,
    };

    private static JsonObject Err(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var err = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (data is not null) err["data"] = data;
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = err,
        };
    }

    private static IResult WriteError(HttpContext ctx, JsonNode? id, int code, string message)
    {
        var obj = Err(id, code, message);
        ctx.Response.ContentType = "application/json";
        ctx.Response.WriteAsync(obj.ToJsonString(Json)).GetAwaiter().GetResult();
        return Results.Empty;
    }
}
