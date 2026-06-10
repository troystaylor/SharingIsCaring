using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class SetMetadataTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "set_metadata",
			Description = "Set SharePoint list-item column values on a drive item (PATCH listItem/fields). Pass a fields object whose keys are the internal column names. Useful for tagging uploaded documents with content types, taxonomies, or custom columns.",
			Annotations = new ToolAnnotations("Set SharePoint item metadata", ReadOnlyHint: false, DestructiveHint: false, IdempotentHint: true),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["drive_id"] = ToolHelpers.Prop("string", "Drive id."),
				["item_id"] = ToolHelpers.Prop("string", "Item id of the file or folder to tag."),
				["fields"] = ToolHelpers.SchemaOpenObject(new JsonObject())
			}, "drive_id", "item_id", "fields"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				return ToolHelpers.ContentResult(await graph.SetListItemFieldsAsync(fields: ToolHelpers.RequireObject(args, "fields"), driveId: ToolHelpers.RequireString(args, "drive_id"), itemId: ToolHelpers.RequireString(args, "item_id"), ct: ct));
			}
		};
	}
}
