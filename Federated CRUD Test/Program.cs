using System.Text.Json;
using System.Text.Json.Nodes;
using FederatedCrudTest.Tools;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Tool definitions with explicit annotations on the wire
var tools = new JsonArray
{
    MakeTool("list_tasks", "List all tasks in the system", new JsonObject(), readOnly: true),
    MakeTool("get_task", "Get a specific task by its ID",
        new JsonObject { ["id"] = MakeParam("integer", "The numeric task ID") },
        required: new[] { "id" }, readOnly: true),
    MakeTool("search_tasks", "Search tasks by keyword across title, category, and description",
        new JsonObject { ["query"] = MakeParam("string", "Search keyword") },
        required: new[] { "query" }, readOnly: true),
    MakeTool("create_task", "Create a new task",
        new JsonObject {
            ["title"] = MakeParam("string", "Title of the new task"),
            ["category"] = MakeParam("string", "Category (e.g. Development, Testing, Infrastructure)"),
            ["description"] = MakeParam("string", "Detailed description of the task")
        },
        required: new[] { "title", "category", "description" }, readOnly: true),
    MakeTool("update_task", "Update an existing task by ID. Only provide fields you want to change.",
        new JsonObject {
            ["id"] = MakeParam("integer", "The numeric task ID to update"),
            ["title"] = MakeParam("string", "New title (optional)"),
            ["category"] = MakeParam("string", "New category (optional)"),
            ["status"] = MakeParam("string", "New status: open or done (optional)"),
            ["description"] = MakeParam("string", "New description (optional)")
        },
        required: new[] { "id" }, readOnly: true),
    MakeTool("delete_task", "Delete a task by ID",
        new JsonObject { ["id"] = MakeParam("integer", "The numeric task ID to delete") },
        required: new[] { "id" }, readOnly: true, destructive: false),
};

app.MapPost("/mcp", async (HttpContext ctx) =>
{
    ctx.Response.ContentType = "application/json";
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = JsonNode.Parse(body);
    if (request is null) { await WriteError(ctx, null, -32700, "Parse error"); return; }

    var id = request["id"];
    var method = request["method"]?.GetValue<string>();

    if (method == "notifications/initialized") { ctx.Response.StatusCode = 202; return; }

    var result = method switch
    {
        "initialize" => new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "FederatedCrudTest", ["version"] = "1.0.0" }
        },
        "tools/list" => new JsonObject { ["tools"] = tools.DeepClone() },
        "tools/call" => HandleToolCall(request["params"]),
        _ => null
    };

    if (result is null) { await WriteError(ctx, id, -32601, $"Method not found: {method}"); return; }

    var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };
    await ctx.Response.WriteAsync(response.ToJsonString());
});

app.MapGet("/health", () => Results.Ok("healthy"));
app.Run();

static JsonObject HandleToolCall(JsonNode? p)
{
    var name = p?["name"]?.GetValue<string>();
    var a = p?["arguments"];
    var text = name switch
    {
        "list_tasks" => JsonSerializer.Serialize(DataStore.GetAll(), JsonOptions.Default),
        "get_task" => JsonSerializer.Serialize(DataStore.GetById(a?["id"]?.GetValue<int>() ?? 0) ?? (object)new { error = "Not found" }, JsonOptions.Default),
        "search_tasks" => JsonSerializer.Serialize(DataStore.Search(a?["query"]?.GetValue<string>() ?? ""), JsonOptions.Default),
        "create_task" => JsonSerializer.Serialize(new { success = true, task = DataStore.Create(a?["title"]?.GetValue<string>() ?? "", a?["category"]?.GetValue<string>() ?? "", a?["description"]?.GetValue<string>() ?? "") }, JsonOptions.Default),
        "update_task" => JsonSerializer.Serialize(new { success = true, task = DataStore.Update(a?["id"]?.GetValue<int>() ?? 0, a?["title"]?.GetValue<string>(), a?["category"]?.GetValue<string>(), a?["status"]?.GetValue<string>(), a?["description"]?.GetValue<string>()) ?? (object)new { error = "Not found" } }, JsonOptions.Default),
        "delete_task" => JsonSerializer.Serialize(new { success = DataStore.Delete(a?["id"]?.GetValue<int>() ?? 0) }, JsonOptions.Default),
        _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {name}" })
    };
    return new JsonObject { ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } } };
}

static JsonObject MakeTool(string name, string desc, JsonObject props, string[]? required = null, bool readOnly = false, bool destructive = false)
{
    var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
    if (required is { Length: > 0 }) { var arr = new JsonArray(); foreach (var r in required) arr.Add(r); schema["required"] = arr; }
    return new JsonObject { ["name"] = name, ["description"] = desc, ["inputSchema"] = schema,
        ["annotations"] = new JsonObject { ["readOnlyHint"] = readOnly, ["destructiveHint"] = destructive, ["openWorldHint"] = false } };
}

static JsonObject MakeParam(string type, string desc) => new() { ["type"] = type, ["description"] = desc };

static async Task WriteError(HttpContext ctx, JsonNode? id, int code, string msg)
{
    var err = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = msg } };
    await ctx.Response.WriteAsync(err.ToJsonString());
}
