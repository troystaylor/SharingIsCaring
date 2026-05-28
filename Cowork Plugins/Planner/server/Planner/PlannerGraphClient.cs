using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlannerCoworkMcp.Auth;

namespace PlannerCoworkMcp.Planner;

public interface IPlannerGraphClient
{
    Task<JsonObject> ListMyTodoListsAsync(CancellationToken ct = default);
    Task<JsonObject> ListMyTodoTasksAsync(string listId, CancellationToken ct = default);
    Task<JsonObject> ListMyPlansAsync(CancellationToken ct = default);
    Task<JsonObject> ListGroupPlansAsync(string groupId, CancellationToken ct = default);
    Task<JsonObject> ListPlanTasksAsync(string planId, CancellationToken ct = default);
    Task<JsonObject> ListPlanBucketsAsync(string planId, CancellationToken ct = default);
    Task<JsonObject> ListMyTasksAsync(CancellationToken ct = default);
    Task<JsonObject> ListUserTasksAsync(string userId, CancellationToken ct = default);
    Task<JsonObject> GetTaskAsync(string taskId, CancellationToken ct = default);
    Task<JsonObject> GetTaskDetailsAsync(string taskId, CancellationToken ct = default);
    Task<JsonObject> CreateTaskAsync(JsonObject body, CancellationToken ct = default);
    Task<JsonObject> PatchTaskAsync(string taskId, string etag, JsonObject changes, CancellationToken ct = default);
    Task<JsonObject> PatchTaskDetailsAsync(string taskId, string etag, JsonObject changes, CancellationToken ct = default);
}

public sealed class PlannerGraphClient : IPlannerGraphClient
{
    private readonly HttpClient _http;
    private readonly IBearerTokenAccessor _token;

    public PlannerGraphClient(HttpClient http, IBearerTokenAccessor token)
    {
        _http = http;
        _token = token;
    }

    public Task<JsonObject> ListMyTodoListsAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, "me/todo/lists", body: null, ifMatch: null, ct);

    public Task<JsonObject> ListMyTodoTasksAsync(string listId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"me/todo/lists/{listId}/tasks", body: null, ifMatch: null, ct);

    public Task<JsonObject> ListMyPlansAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, "me/planner/plans", body: null, ifMatch: null, ct);

    public Task<JsonObject> ListGroupPlansAsync(string groupId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"groups/{groupId}/planner/plans", body: null, ifMatch: null, ct);

    public Task<JsonObject> ListPlanTasksAsync(string planId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"planner/plans/{planId}/tasks", body: null, ifMatch: null, ct);

    public Task<JsonObject> ListPlanBucketsAsync(string planId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"planner/plans/{planId}/buckets", body: null, ifMatch: null, ct);

    public Task<JsonObject> ListMyTasksAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, "me/planner/tasks", body: null, ifMatch: null, ct);

    public Task<JsonObject> ListUserTasksAsync(string userId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"users/{userId}/planner/tasks", body: null, ifMatch: null, ct);

    public Task<JsonObject> GetTaskAsync(string taskId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"planner/tasks/{taskId}", body: null, ifMatch: null, ct);

    public Task<JsonObject> GetTaskDetailsAsync(string taskId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"planner/tasks/{taskId}/details", body: null, ifMatch: null, ct);

    public Task<JsonObject> CreateTaskAsync(JsonObject body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "planner/tasks", body, ifMatch: null, ct);

    public async Task<JsonObject> PatchTaskAsync(string taskId, string etag, JsonObject changes, CancellationToken ct = default)
    {
        await SendAsync(HttpMethod.Patch, $"planner/tasks/{taskId}", changes, etag, ct, allowNoContent: true);
        return new JsonObject
        {
            ["success"] = true,
            ["id"] = taskId,
            ["etag"] = etag,
            ["updatedFields"] = changes.DeepClone(),
        };
    }

    public async Task<JsonObject> PatchTaskDetailsAsync(string taskId, string etag, JsonObject changes, CancellationToken ct = default)
    {
        await SendAsync(HttpMethod.Patch, $"planner/tasks/{taskId}/details", changes, etag, ct, allowNoContent: true);
        return new JsonObject
        {
            ["success"] = true,
            ["id"] = taskId,
            ["etag"] = etag,
            ["updatedFields"] = changes.DeepClone(),
        };
    }

    private async Task<JsonObject> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        string? ifMatch,
        CancellationToken ct,
        bool allowNoContent = false)
    {
        var bearer = _token.Require();

        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
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
            throw new PlannerApiException(path, (int)resp.StatusCode, raw);
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

        return new JsonObject { ["value"] = parsed?.DeepClone() };
    }
}
