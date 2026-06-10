using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class CreateFolderTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "create_folder",
			Description = "Create a new folder inside a drive. parent_path is relative to the drive root (use '' or '/' for root). conflict_behavior: 'rename' (default), 'replace', or 'fail'.",
			Annotations = new ToolAnnotations("Create SharePoint folder"),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["drive_id"] = ToolHelpers.Prop("string", "Drive id from list_drives."),
				["parent_path"] = ToolHelpers.Prop("string", "Parent folder path relative to drive root (e.g. 'General' or '' for root)."),
				["name"] = ToolHelpers.Prop("string", "New folder name."),
				["conflict_behavior"] = ToolHelpers.Prop("string", "rename | replace | fail. Default 'rename'.")
			}, "drive_id", "name"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				return ToolHelpers.ContentResult(await graph.CreateFolderAsync(ToolHelpers.RequireString(args, "drive_id"), ToolHelpers.OptString(args, "parent_path") ?? string.Empty, ToolHelpers.RequireString(args, "name"), ToolHelpers.OptString(args, "conflict_behavior") ?? "rename", ct));
			}
		};
	}
}
