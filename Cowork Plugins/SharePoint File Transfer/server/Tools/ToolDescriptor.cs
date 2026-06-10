using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SharePointTransferMcp.Tools;

public sealed class ToolDescriptor
{
	public required string Name { get; init; }

	public required string Description { get; init; }

	public required ToolAnnotations Annotations { get; init; }

	public required JsonObject InputSchema { get; init; }

	public required Func<IServiceProvider, JsonObject, CancellationToken, Task<JsonObject>> Invoke { get; init; }

	public Task<JsonObject> InvokeAsync(IServiceProvider sp, JsonObject args, CancellationToken ct)
	{
		return Invoke(sp, args, ct);
	}

	public JsonObject ToToolDescriptor()
	{
		JsonObject jsonObject = new JsonObject
		{
			["readOnlyHint"] = Annotations.ReadOnlyHint,
			["destructiveHint"] = Annotations.DestructiveHint,
			["idempotentHint"] = Annotations.IdempotentHint,
			["openWorldHint"] = Annotations.OpenWorldHint
		};
		if (!string.IsNullOrEmpty(Annotations.Title))
		{
			jsonObject["title"] = Annotations.Title;
		}
		return new JsonObject
		{
			["name"] = Name,
			["description"] = Description,
			["inputSchema"] = InputSchema.DeepClone(),
			["annotations"] = jsonObject
		};
	}
}
