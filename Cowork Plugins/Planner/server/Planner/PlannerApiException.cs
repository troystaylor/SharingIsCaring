using System.Text.Json.Nodes;

namespace PlannerCoworkMcp.Planner;

public sealed class PlannerApiException : Exception
{
    public string Path { get; }
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public PlannerApiException(string path, int statusCode, string responseBody)
        : base($"Planner API call failed ({statusCode}) at '{path}'.")
    {
        Path = path;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public JsonObject ToData()
    {
        return new JsonObject
        {
            ["path"] = Path,
            ["statusCode"] = StatusCode,
            ["response"] = ResponseBody,
        };
    }
}
