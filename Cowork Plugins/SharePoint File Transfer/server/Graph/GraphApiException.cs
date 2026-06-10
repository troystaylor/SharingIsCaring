using System;
using System.Text.Json.Nodes;

namespace SharePointTransferMcp.Graph;

public sealed class GraphApiException : Exception
{
	public string Endpoint { get; }

	public int StatusCode { get; }

	public string? ResponseBody { get; }

	public GraphApiException(string endpoint, int statusCode, string? responseBody)
		: base($"graph api error from {endpoint}: http_{statusCode}")
	{
		Endpoint = endpoint;
		StatusCode = statusCode;
		ResponseBody = responseBody;
	}

	public JsonObject ToData()
	{
		return new JsonObject
		{
			["endpoint"] = Endpoint,
			["status_code"] = StatusCode,
			["response"] = ResponseBody
		};
	}
}
