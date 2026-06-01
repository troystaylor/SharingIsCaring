using System.Text.Json.Nodes;

namespace SalesforceCoworkMcp.Salesforce;

public sealed class SalesforceApiException : Exception
{
    public string Endpoint { get; }
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public SalesforceApiException(string endpoint, int statusCode, string? responseBody)
        : base($"salesforce api error from {endpoint}: http_{statusCode}")
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
