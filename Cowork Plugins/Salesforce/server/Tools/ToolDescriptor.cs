using System.Text.Json.Nodes;

namespace SalesforceCoworkMcp.Tools;

public sealed record ToolAnnotations(
    string? Title = null,
    bool ReadOnlyHint = false,
    bool DestructiveHint = false,
    bool IdempotentHint = false,
    bool OpenWorldHint = true);

public sealed class ToolDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ToolAnnotations Annotations { get; init; }
    public required JsonObject InputSchema { get; init; }
    public required Func<IServiceProvider, JsonObject, CancellationToken, Task<JsonObject>> Invoke { get; init; }

    public Task<JsonObject> InvokeAsync(IServiceProvider sp, JsonObject args, CancellationToken ct)
        => Invoke(sp, args, ct);

    public JsonObject ToToolDescriptor()
    {
        var ann = new JsonObject
        {
            ["readOnlyHint"] = Annotations.ReadOnlyHint,
            ["destructiveHint"] = Annotations.DestructiveHint,
            ["idempotentHint"] = Annotations.IdempotentHint,
            ["openWorldHint"] = Annotations.OpenWorldHint,
        };
        if (!string.IsNullOrEmpty(Annotations.Title)) ann["title"] = Annotations.Title;

        return new JsonObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = InputSchema.DeepClone(),
            ["annotations"] = ann,
        };
    }
}
