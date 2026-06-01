using System.Text.Json.Nodes;

namespace NetSuiteCoworkMcp.NetSuite;

public sealed class NetSuiteApiException : Exception
{
    public string Endpoint { get; }
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public NetSuiteApiException(string endpoint, int statusCode, string? responseBody)
        : base($"netsuite api error from {endpoint}: http_{statusCode}")
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public JsonObject ToData() => new()
    {
        ["endpoint"] = Endpoint,
        ["status_code"] = StatusCode,
        ["response"] = ResponseBody,
    };
}
