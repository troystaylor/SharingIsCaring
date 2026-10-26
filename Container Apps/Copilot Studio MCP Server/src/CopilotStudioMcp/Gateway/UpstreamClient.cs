using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CopilotStudioMcp.Configuration;
using Microsoft.Extensions.Options;

namespace CopilotStudioMcp.Gateway;

/// <summary>A failure the caller can be told about. Its message is safe to show to the model and the user.</summary>
public sealed class GatewayException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;
}

public sealed record EnvironmentInfo(string Id, string? DisplayName, string? Type, string? Region);

/// <summary>Builds Copilot Studio endpoints. The only host the gateway ever calls is derived here from a checked GUID.</summary>
public static partial class Environments
{
    public const string HostSuffix = "environment.api.powerplatform.com";
    public const string DiscoveryUrl = "https://api.powerplatform.com/environmentmanagement/environments?api-version=1";

    [GeneratedRegex("^(?:default-)?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex IdPattern();

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    public static Uri McpUrl(string? environmentId)
    {
        if (!IsValidId(environmentId))
        {
            throw new GatewayException(400, "INVALID_ENVIRONMENT", "environmentId must be an ID returned by list_environments.");
        }

        var encoded = environmentId!.Replace("-", string.Empty).ToLowerInvariant();
        return new Uri($"https://{encoded[..^2]}.{encoded[^2..]}.{HostSuffix}/copilotflows/agenticbuilder/mcp?api-version=1");
    }
}

/// <summary>Calls Copilot Studio and environment discovery with the caller's own access token.</summary>
public sealed class UpstreamClient(HttpClient http, IOptions<GatewayOptions> options)
{
    private readonly int _maxBytes = options.Value.MaxResponseBytes;

    public async Task<IReadOnlyList<EnvironmentInfo>> ListEnvironmentsAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Environments.DiscoveryUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");

        var (status, _, body) = await SendAsync(request, ct);
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new GatewayException(
                (int)status,
                "FORBIDDEN",
                "Listing environments was denied. The plugin needs the EnvironmentManagement.Environments.Read permission; ask an admin to grant consent.");
        }

        if (status != HttpStatusCode.OK)
        {
            throw StatusError(status, "Environment discovery");
        }

        var list = new List<EnvironmentInfo>();
        foreach (var item in (JsonNode.Parse(body)?["value"] as JsonArray) ?? [])
        {
            var id = item?["id"]?.GetValue<string>();
            var state = item?["state"]?.GetValue<string>() ?? "Enabled";
            if (Environments.IsValidId(id) && state == "Enabled")
            {
                list.Add(new EnvironmentInfo(
                    id!,
                    item?["displayName"]?.GetValue<string>(),
                    item?["type"]?.GetValue<string>(),
                    item?["azureRegion"]?.GetValue<string>() ?? item?["geo"]?.GetValue<string>()));
            }
        }

        return list;
    }

    /// <summary>Sends one JSON-RPC request to an environment and returns the matching response message.</summary>
    public async Task<JsonObject> CallAsync(string environmentId, string token, string method, JsonNode? parameters, CancellationToken ct)
    {
        var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters };
        using var request = new HttpRequestMessage(HttpMethod.Post, Environments.McpUrl(environmentId))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        var (status, contentType, text) = await SendAsync(request, ct);
        var code = (int)status;
        if (code < 200 || code >= 300)
        {
            throw StatusError(status, "Copilot Studio");
        }

        return ParseRpcResponse(contentType, text);
    }

    private async Task<(HttpStatusCode Status, string? ContentType, string Body)> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var text = await response.Content.ReadAsStringAsync(ct);
            if (Encoding.UTF8.GetByteCount(text) > _maxBytes)
            {
                throw new GatewayException(502, "UPSTREAM_TOO_LARGE", "The Copilot Studio response was too large to relay.");
            }

            return (response.StatusCode, contentType, text);
        }
        catch (GatewayException)
        {
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            throw new GatewayException(504, "UPSTREAM_TIMEOUT", "Copilot Studio did not respond in time.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GatewayException(504, "UPSTREAM_TIMEOUT", "Copilot Studio did not respond in time.");
        }
        catch (Exception ex) when (ex is HttpRequestException or Polly.CircuitBreaker.BrokenCircuitException or IOException)
        {
            throw new GatewayException(502, "UPSTREAM_UNREACHABLE", "Copilot Studio could not be reached.");
        }
    }

    private static GatewayException StatusError(HttpStatusCode status, string what) => (int)status switch
    {
        401 => new(401, "UNAUTHORIZED", $"{what} rejected the sign-in. Sign in again."),
        403 => new(403, "FORBIDDEN", $"{what} denied access for this account."),
        404 => new(404, "NOT_FOUND", $"{what} was not found. Check the environment."),
        _ => new(502, "UPSTREAM_ERROR", $"{what} returned HTTP {(int)status}.")
    };

    /// <summary>Reads a JSON-RPC response from either a JSON body or a text/event-stream body.</summary>
    public static JsonObject ParseRpcResponse(string? contentType, string text)
    {
        if (contentType is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = JsonNode.Parse(text);
            if (parsed is JsonObject single && IsResponse(single))
            {
                return single;
            }

            if (parsed is JsonArray batch && batch.OfType<JsonObject>().FirstOrDefault(IsResponse) is { } found)
            {
                return found;
            }
        }
        else
        {
            foreach (var block in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var data = string.Join('\n', block.Split('\n')
                    .Where(l => l.StartsWith("data:", StringComparison.Ordinal))
                    .Select(l => l["data:".Length..].TrimStart()));
                if (data.Length == 0)
                {
                    continue;
                }

                try
                {
                    if (JsonNode.Parse(data) is JsonObject message && IsResponse(message))
                    {
                        return message;
                    }
                }
                catch (JsonException)
                {
                    // Not a JSON event; keep looking.
                }
            }
        }

        throw new GatewayException(502, "UPSTREAM_BAD_RESPONSE", "Copilot Studio returned an unexpected response.");
    }

    private static bool IsResponse(JsonObject message) =>
        message.ContainsKey("id") && (message.ContainsKey("result") || message.ContainsKey("error"));
}
