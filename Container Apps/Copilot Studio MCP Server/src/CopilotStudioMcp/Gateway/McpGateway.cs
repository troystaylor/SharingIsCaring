using System.Text.Json;
using System.Text.Json.Nodes;

namespace CopilotStudioMcp.Gateway;

/// <summary>Handles the MCP JSON-RPC methods the gateway serves. Stateless: every request stands alone.</summary>
public sealed class McpGateway(UpstreamClient upstream, ToolCatalog catalog, ITokenBroker broker, ILogger<McpGateway> logger)
{
    private static readonly string[] ProtocolVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    private const string Instructions =
        "This is the Microsoft Copilot Studio gateway. It reaches Copilot Studio in any Power Platform environment the signed-in user can access.\n" +
        "Every tool except list_environments requires `environmentId`. Call list_environments first, confirm the environment with the user when more than one could apply, and pass the same `environmentId` for the whole task.\n" +
        "Agents, workflows, connections and credit consumption are scoped to the environment you pass. Do not mix IDs from different environments in one task.\n" +
        "Use get_copilot_studio_skills before authoring, and discover_resources with action=describe_actions to read an action contract before using it.";

    public sealed record Reply(int Status, JsonNode? Body, string? WwwAuthenticate = null);

    /// <param name="cacheKey">Scopes the cached tool list, normally to the caller's tenant.</param>
    public async Task<Reply> HandleAsync(JsonNode? message, string token, string cacheKey, CancellationToken ct)
    {
        if (message is not JsonObject request)
        {
            return new(400, Error(null, -32600, "Send a single JSON-RPC message."));
        }

        // A message with no id is a notification and gets no reply.
        if (!request.ContainsKey("id"))
        {
            return new(202, null);
        }

        var id = request["id"]?.DeepClone();
        var method = request["method"]?.GetValue<string>();
        var parameters = request["params"] as JsonObject;
        var tool = method == "tools/call" ? parameters?["name"]?.GetValue<string>() : null;
        var started = TimeProvider.System.GetTimestamp();

        try
        {
            JsonNode? result;
            switch (method)
            {
                case "initialize":
                    var requested = parameters?["protocolVersion"]?.GetValue<string>();
                    result = new JsonObject
                    {
                        ["protocolVersion"] = ProtocolVersions.Contains(requested) ? requested : ProtocolVersions[0],
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                        ["serverInfo"] = new JsonObject { ["name"] = "copilot-studio-gateway", ["version"] = "1.0.0" },
                        ["instructions"] = Instructions
                    };
                    break;
                case "ping":
                    result = new JsonObject();
                    break;
                case "tools/list":
                    result = new JsonObject { ["tools"] = await catalog.GetAsync(cacheKey, await broker.ExchangeAsync(token, ct), ct) };
                    break;
                case "tools/call":
                    var powerPlatformToken = await broker.ExchangeAsync(token, ct);
                    var forwarded = await CallToolAsync(parameters, powerPlatformToken, ct);
                    if (forwarded is not null)
                    {
                        forwarded["id"] = id;
                        return new(200, forwarded);
                    }

                    result = ToolText(JsonSerializer.Serialize(new { environments = await upstream.ListEnvironmentsAsync(powerPlatformToken, ct) }, Pretty), false);
                    break;
                default:
                    return new(200, Error(id, -32601, $"Method not found: {method}"));
            }

            return new(200, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
        }
        catch (GatewayException ex)
        {
            logger.LogWarning("Request failed: method={Method} tool={Tool} code={Code}", method, tool, ex.Code);
            if (ex.Status == 401)
            {
                return new(401, Error(id, -32001, ex.Message), "Bearer");
            }

            return method == "tools/call"
                ? new(200, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = ToolText(ex.Message, true) })
                : new(200, Error(id, -32000, ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected failure: method={Method} tool={Tool}", method, tool);
            return method == "tools/call"
                ? new(200, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = ToolText("The gateway hit an unexpected error.", true) })
                : new(200, Error(id, -32603, "The gateway hit an unexpected error."));
        }
        finally
        {
            logger.LogInformation("Handled {Method} {Tool} in {Ms} ms", method, tool, (int)TimeProvider.System.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>Returns the upstream reply, or null when the call is the gateway's own list_environments tool.</summary>
    private async Task<JsonObject?> CallToolAsync(JsonObject? parameters, string token, CancellationToken ct)
    {
        var name = parameters?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
        {
            throw new GatewayException(400, "INVALID_PARAMS", "Tool name is required.");
        }

        if (name == ToolCatalog.ListEnvironmentsName)
        {
            return null;
        }

        var forwardedParams = (JsonObject)parameters!.DeepClone();
        var arguments = forwardedParams["arguments"] as JsonObject ?? new JsonObject();
        var environmentId = arguments[ToolCatalog.EnvironmentIdName]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(environmentId))
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["result"] = ToolText("environmentId is required. Call list_environments and pass one of the returned IDs.", true)
            };
        }

        arguments.Remove(ToolCatalog.EnvironmentIdName);
        forwardedParams["arguments"] = arguments;
        return await upstream.CallAsync(environmentId, token, "tools/call", forwardedParams, ct);
    }

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static JsonObject ToolText(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
    };
}
