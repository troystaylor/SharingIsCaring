using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class MoveItemTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "move_item",
			Description = "Move and/or rename an item within the same drive. Supply new_parent_id (destination folder id) and/or new_name. At least one is required.",
			Annotations = new ToolAnnotations("Move or rename SharePoint item", ReadOnlyHint: false, DestructiveHint: true),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["drive_id"] = ToolHelpers.Prop("string", "Drive id."),
				["item_id"] = ToolHelpers.Prop("string", "Id of the item to move."),
				["new_parent_id"] = ToolHelpers.Prop("string", "Destination folder id within the same drive."),
				["new_name"] = ToolHelpers.Prop("string", "New file or folder name.")
			}, "drive_id", "item_id"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				string newParent = ToolHelpers.OptString(args, "new_parent_id");
				string newName = ToolHelpers.OptString(args, "new_name");
				if (string.IsNullOrEmpty(newParent) && string.IsNullOrEmpty(newName))
				{
					throw new ArgumentException("must supply new_parent_id or new_name");
				}
				return ToolHelpers.ContentResult(await graph.MoveItemAsync(ToolHelpers.RequireString(args, "drive_id"), ToolHelpers.RequireString(args, "item_id"), newParent, newName, ct));
			}
		};
	}
}
