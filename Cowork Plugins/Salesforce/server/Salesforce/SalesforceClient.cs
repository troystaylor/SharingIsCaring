using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using SalesforceCoworkMcp.Auth;

namespace SalesforceCoworkMcp.Salesforce;

public interface ISalesforceClient
{
    Task<JsonObject> QueryAsync(string soql, CancellationToken ct = default);
    Task<JsonObject> GetSObjectAsync(string objectName, string id, string? fieldsCsv = null, CancellationToken ct = default);
    Task<JsonObject> PatchSObjectAsync(string objectName, string id, JsonObject fields, CancellationToken ct = default);
    Task<JsonObject> CreateSObjectAsync(string objectName, JsonObject body, CancellationToken ct = default);
}

public sealed class SalesforceClient : ISalesforceClient
{
    private readonly HttpClient _http;
    private readonly IBearerTokenAccessor _token;
    private readonly string _apiVersion;

    public SalesforceClient(HttpClient http, IBearerTokenAccessor token, IConfiguration config)
    {
        _http = http;
        _token = token;
        _apiVersion = config["Salesforce:ApiVersion"]
            ?? Environment.GetEnvironmentVariable("SALESFORCE_API_VERSION")
            ?? "v61.0";
    }

    public async Task<JsonObject> QueryAsync(string soql, CancellationToken ct = default)
    {
        var path = $"services/data/{_apiVersion}/query?q={Uri.EscapeDataString(soql)}";
        return await SendAsync(HttpMethod.Get, path, body: null, ct);
    }

    public async Task<JsonObject> GetSObjectAsync(string objectName, string id, string? fieldsCsv = null, CancellationToken ct = default)
    {
        var basePath = $"services/data/{_apiVersion}/sobjects/{objectName}/{id}";
        var path = string.IsNullOrWhiteSpace(fieldsCsv)
            ? basePath
            : $"{basePath}?fields={Uri.EscapeDataString(fieldsCsv)}";
        return await SendAsync(HttpMethod.Get, path, body: null, ct);
    }

    public async Task<JsonObject> PatchSObjectAsync(string objectName, string id, JsonObject fields, CancellationToken ct = default)
    {
        var path = $"services/data/{_apiVersion}/sobjects/{objectName}/{id}";
        await SendAsync(HttpMethod.Patch, path, fields, ct, allowNoContent: true);

        return new JsonObject
        {
            ["success"] = true,
            ["id"] = id,
            ["object"] = objectName,
            ["updatedFields"] = fields.DeepClone(),
        };
    }

    public async Task<JsonObject> CreateSObjectAsync(string objectName, JsonObject body, CancellationToken ct = default)
    {
        var path = $"services/data/{_apiVersion}/sobjects/{objectName}";
        return await SendAsync(HttpMethod.Post, path, body, ct);
    }

    private async Task<JsonObject> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct, bool allowNoContent = false)
    {
        var bearer = _token.Require();

        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        if (body is not null)
        {
            req.Content = new StringContent(
                body.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
                Encoding.UTF8,
                "application/json");
        }

        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var status = (int)resp.StatusCode;
            var sfErrorCode = TryExtractSalesforceErrorCode(raw);

            if (status == 401 || status == 403 || IsSalesforceAuthErrorCode(sfErrorCode))
            {
                var detail = string.IsNullOrEmpty(sfErrorCode)
                    ? $"Salesforce rejected the forwarded token (http {status})"
                    : $"Salesforce rejected the forwarded token: {sfErrorCode} (http {status})";
                throw new UnauthorizedSalesforceException(
                    sfErrorCode ?? "rejected_by_salesforce",
                    detail);
            }

            throw new SalesforceApiException(path, status, raw);
        }

        if (allowNoContent && resp.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return new JsonObject { ["success"] = true };
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new JsonObject();
        }

        var parsed = JsonNode.Parse(raw);
        if (parsed is JsonObject obj)
        {
            return obj;
        }

        return new JsonObject
        {
            ["value"] = parsed?.DeepClone(),
        };
    }

    private static string? TryExtractSalesforceErrorCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var node = JsonNode.Parse(body);
            // Salesforce REST errors are typically: [{"message":"...","errorCode":"INVALID_SESSION_ID"}]
            if (node is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject first)
            {
                return first["errorCode"]?.GetValue<string>();
            }
            if (node is JsonObject obj)
            {
                return obj["errorCode"]?.GetValue<string>()
                    ?? obj["error"]?.GetValue<string>();
            }
        }
        catch
        {
            // not JSON or unexpected shape — ignore
        }
        return null;
    }

    private static bool IsSalesforceAuthErrorCode(string? code) => code switch
    {
        "INVALID_SESSION_ID" => true,
        "SESSION_NOT_FOUND" => true,
        "INVALID_LOGIN" => true,
        "INVALID_AUTH_HEADER" => true,
        "MISSING_OAUTH_TOKEN" => true,
        "invalid_token" => true,
        "invalid_grant" => true,
        _ => false,
    };
}
