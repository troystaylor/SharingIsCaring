using System.Text.Json.Nodes;

namespace SlackCoworkMcp.Slack;

/// <summary>
/// Slack Web API responses are HTTP 200 with <c>{ "ok": false, "error": "..." }</c>
/// on failure. The client parses that and throws this; the MCP handler surfaces
/// it as JSON-RPC error <c>-32010</c> with the raw Slack error in <c>data</c>.
/// </summary>
public sealed class SlackApiException : Exception
{
    public string SlackError { get; }
    public string Endpoint { get; }
    public JsonNode? Response { get; }

    public SlackApiException(string endpoint, string slackError, JsonNode? response)
        : base($"slack api error from {endpoint}: {slackError}")
    {
        Endpoint = endpoint;
        SlackError = slackError;
        Response = response;
    }

    public JsonObject ToData() => new()
    {
        ["endpoint"] = Endpoint,
        ["slack_error"] = SlackError,
        ["response"] = Response?.DeepClone(),
    };
}
