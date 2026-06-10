using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class CopyItemTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "copy_item",
			Description = "Copy a file or folder to another location. Supports cross-site and cross-library copies. Specify the source by drive_id + item_id, and the destination by dest_drive_id + dest_folder_id. The copy runs asynchronously on the server; use check_copy_status with the returned monitor_url to track progress.",
			Annotations = new ToolAnnotations("Copy SharePoint item", ReadOnlyHint: false),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["drive_id"] = ToolHelpers.Prop("string", "Source drive id."),
				["item_id"] = ToolHelpers.Prop("string", "Id of the item to copy."),
				["dest_drive_id"] = ToolHelpers.Prop("string", "Destination drive id. Can be a different site's drive for cross-site copies."),
				["dest_folder_id"] = ToolHelpers.Prop("string", "Destination folder id in the target drive."),
				["new_name"] = ToolHelpers.Prop("string", "Optional new name for the copied item."),
				["conflict_behavior"] = ToolHelpers.Prop("string", "rename | replace | fail (default: rename).")
			}, "drive_id", "item_id", "dest_drive_id", "dest_folder_id"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				string driveId = ToolHelpers.RequireString(args, "drive_id");
				string itemId = ToolHelpers.RequireString(args, "item_id");
				string destDriveId = ToolHelpers.RequireString(args, "dest_drive_id");
				string destFolderId = ToolHelpers.RequireString(args, "dest_folder_id");
				string? newName = ToolHelpers.OptString(args, "new_name");
				string conflict = ToolHelpers.OptString(args, "conflict_behavior") ?? "rename";

				string monitorUrl = await graph.CopyItemAsync(driveId, itemId, destDriveId, destFolderId, newName, conflict, ct);

				var result = new JsonObject
				{
					["status"] = "accepted",
					["message"] = "Copy operation queued. Use check_copy_status with the monitor_url to track progress.",
					["monitor_url"] = monitorUrl
				};
				return ToolHelpers.ContentResult(result);
			}
		};
	}
}
