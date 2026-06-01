using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetSuiteCoworkMcp.Auth;

namespace NetSuiteCoworkMcp.NetSuite;

public interface INetSuiteClient
{
    Task<JsonObject> SuiteQLAsync(string query, int? limit = null, int? offset = null, CancellationToken ct = default);
    Task<JsonObject> ListRecordsAsync(string recordType, string? filter = null, string? fieldsCsv = null, int? limit = null, int? offset = null, bool expand = false, CancellationToken ct = default);
    Task<JsonObject> GetRecordAsync(string recordType, string recordId, string? fieldsCsv = null, bool expand = false, CancellationToken ct = default);
    Task<JsonObject> CreateRecordAsync(string recordType, JsonObject body, CancellationToken ct = default);
    Task<JsonObject> UpdateRecordAsync(string recordType, string recordId, JsonObject body, CancellationToken ct = default);
    Task<JsonObject> DeleteRecordAsync(string recordType, string recordId, CancellationToken ct = default);
    Task<JsonObject> ListRecordTypesAsync(CancellationToken ct = default);
    Task<JsonObject> GetRecordMetadataAsync(string recordType, CancellationToken ct = default);
    Task<JsonObject> GetSublistAsync(string recordType, string recordId, string sublistId, CancellationToken ct = default);
    Task<JsonObject> AddSublistLineAsync(string recordType, string recordId, string sublistId, JsonObject body, CancellationToken ct = default);
    Task<JsonObject> UpdateSublistLineAsync(string recordType, string recordId, string sublistId, string lineId, JsonObject body, CancellationToken ct = default);
    Task<JsonObject> DeleteSublistLineAsync(string recordType, string recordId, string sublistId, string lineId, CancellationToken ct = default);
}

public sealed class NetSuiteClient : INetSuiteClient
{
    private readonly HttpClient _http;
    private readonly IBearerTokenAccessor _token;

    public NetSuiteClient(HttpClient http, IBearerTokenAccessor token)
    {
        _http = http;
        _token = token;
    }

    public Task<JsonObject> SuiteQLAsync(string query, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        var lim = (limit ?? 100).ToString();
        var off = (offset ?? 0).ToString();
        var path = $"query/v1/suiteql?limit={Uri.EscapeDataString(lim)}&offset={Uri.EscapeDataString(off)}";
        var body = new JsonObject { ["q"] = query };
        return SendAsync(HttpMethod.Post, path, body, ct, preferTransient: true);
    }

    public Task<JsonObject> ListRecordsAsync(string recordType, string? filter = null, string? fieldsCsv = null, int? limit = null, int? offset = null, bool expand = false, CancellationToken ct = default)
    {
        var qp = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter)) qp.Add("q=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(fieldsCsv)) qp.Add("fields=" + Uri.EscapeDataString(fieldsCsv));
        qp.Add("limit=" + Uri.EscapeDataString((limit ?? 100).ToString()));
        if (offset.HasValue && offset.Value > 0) qp.Add("offset=" + offset.Value);
        if (expand) qp.Add("expandSubResources=true");

        var path = $"record/v1/{Uri.EscapeDataString(recordType)}?{string.Join('&', qp)}";
        return SendAsync(HttpMethod.Get, path, body: null, ct);
    }

    public Task<JsonObject> GetRecordAsync(string recordType, string recordId, string? fieldsCsv = null, bool expand = false, CancellationToken ct = default)
    {
        var qp = new List<string>();
        if (!string.IsNullOrWhiteSpace(fieldsCsv)) qp.Add("fields=" + Uri.EscapeDataString(fieldsCsv));
        if (expand) qp.Add("expandSubResources=true");

        var path = $"record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}"
            + (qp.Count > 0 ? "?" + string.Join('&', qp) : "");
        return SendAsync(HttpMethod.Get, path, body: null, ct);
    }

    public Task<JsonObject> CreateRecordAsync(string recordType, JsonObject body, CancellationToken ct = default)
    {
        var path = $"record/v1/{Uri.EscapeDataString(recordType)}";
        return SendAsync(HttpMethod.Post, path, body, ct);
    }

    public Task<JsonObject> UpdateRecordAsync(string recordType, string recordId, JsonObject body, CancellationToken ct = default)
    {
        var path = $"record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}";
        return SendAsync(new HttpMethod("PATCH"), path, body, ct, allowNoContent: true);
    }

    public Task<JsonObject> DeleteRecordAsync(string recordType, string recordId, CancellationToken ct = default)
    {
        var path = $"record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}";
        return SendAsync(HttpMethod.Delete, path, body: null, ct, allowNoContent: true);
    }

    public Task<JsonObject> ListRecordTypesAsync(CancellationToken ct = default)
    {
        return SendAsync(HttpMethod.Get, "record/v1/metadata-catalog", body: null, ct, schemaAccept: true);
    }

    public Task<JsonObject> GetRecordMetadataAsync(string recordType, CancellationToken ct = default)
    {
        var path = $"record/v1/metadata-catalog/{Uri.EscapeDataString(recordType)}";
        return SendAsync(HttpMethod.Get, path, body: null, ct, schemaAccept: true);
    }

    public Task<JsonObject> GetSublistAsync(string recordType, string recordId, string sublistId, CancellationToken ct = default)
    {
        var path = $"record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}";
        return SendAsync(HttpMethod.Get, path, body: null, ct);
    }

    public Task<JsonObject> AddSublistLineAsync(string recordType, string recordId, string sublistId, JsonObject body, CancellationToken ct = default)
    {
        var path = $"record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}";
        return SendAsync(HttpMethod.Post, path, body, ct);
    }

    public Task<JsonObject> UpdateSublistLineAsync(string recordType, string recordId, string sublistId, string lineId, JsonObject body, CancellationToken ct = default)
    {
        var path = $"record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}/{Uri.EscapeDataString(lineId)}";
        return SendAsync(new HttpMethod("PATCH"), path, body, ct, allowNoContent: true);
    }

    public Task<JsonObject> DeleteSublistLineAsync(string recordType, string recordId, string sublistId, string lineId, CancellationToken ct = default)
    {
        var path = $"record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}/{Uri.EscapeDataString(lineId)}";
        return SendAsync(HttpMethod.Delete, path, body: null, ct, allowNoContent: true);
    }

    private async Task<JsonObject> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        CancellationToken ct,
        bool allowNoContent = false,
        bool preferTransient = false,
        bool schemaAccept = false)
    {
        var bearer = _token.Require();

        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (preferTransient)
        {
            req.Headers.TryAddWithoutValidation("Prefer", "transient");
        }
        if (schemaAccept)
        {
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/schema+json"));
        }

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
            if (status == 401 || status == 403)
            {
                var detail = $"NetSuite rejected the forwarded token (http {status})";
                throw new UnauthorizedNetSuiteException("rejected_by_netsuite", detail);
            }
            throw new NetSuiteApiException(path, status, raw);
        }

        if (allowNoContent && resp.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return new JsonObject
            {
                ["success"] = true,
                ["status"] = 204,
            };
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
        return new JsonObject { ["value"] = parsed?.DeepClone() };
    }
}
