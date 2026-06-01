using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetSuiteCoworkMcp.Auth;
using NetSuiteCoworkMcp.NetSuite;
using NetSuiteCoworkMcp.Tools;

namespace NetSuiteCoworkMcp.Endpoints;

public static class McpEndpoints
{
    private const string AuthChallengeKey = "mcp.authChallenge";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static IEndpointRouteBuilder MapMcpRoute(
        this IEndpointRouteBuilder app,
        string path,
        IReadOnlySet<string>? filter)
    {
        app.MapPost(path, async (HttpContext ctx) =>
        {
            var registry = ctx.RequestServices.GetRequiredService<ToolRegistry>();
            var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Mcp");
            var tokens = ctx.RequestServices.GetRequiredService<IBearerTokenAccessor>();

            var diag = tokens.Diagnose();
            logger.LogInformation(
                "mcp request route={Route} token_present={Present} source={Source} shape={Shape} len={Len} exp_in_s={Remaining}",
                path, diag.Present, diag.Source, diag.Shape ?? "n/a", diag.Length, diag.SecondsUntilExpiry?.ToString() ?? "n/a");

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

            JsonNode responsePayload;
            if (body is JsonArray arr)
            {
                var results = new JsonArray();
                foreach (var item in arr)
                {
                    var r = await HandleAsync(ctx, item, registry, filter);
                    if (r is not null) results.Add(r);
                }
                responsePayload = results;
            }
            else
            {
                var resp = await HandleAsync(ctx, body, registry, filter);
                if (resp is null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                    return Results.Empty;
                }
                responsePayload = resp;
            }

            if (ctx.Items.TryGetValue(AuthChallengeKey, out var challengeObj)
                && challengeObj is AuthChallenge challenge)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.Append("WWW-Authenticate",
                    $"Bearer error=\"invalid_token\", error_description=\"{EscapeHeader(challenge.Description)}\"");
                logger.LogWarning(
                    "mcp auth challenge issued route={Route} code={Code} desc={Desc}",
                    path, challenge.Code, challenge.Description);
            }

            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(responsePayload.ToJsonString(Json));
            return Results.Empty;
        });

        return app;
    }

    private sealed record AuthChallenge(string Code, string Description);

    private static string EscapeHeader(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
        catch (UnauthorizedNetSuiteException ex)
        {
            ctx.Items[AuthChallengeKey] = new AuthChallenge(ex.ErrorCode, ex.Message);
            return Err(id, -32001, ex.Message, new JsonObject
            {
                ["error_code"] = ex.ErrorCode,
                ["action"] = "reauthorize_netsuite_connector",
            });
        }
        catch (NetSuiteApiException ex)
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
                ["name"] = "netsuite-cowork-mcp",
                ["version"] = "0.1.0",
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
