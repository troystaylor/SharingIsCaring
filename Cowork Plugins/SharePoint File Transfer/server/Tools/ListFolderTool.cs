using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class ListFolderTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "list_folder",
			Description = "List items in a folder within a drive. Provide either item_path (relative to the drive root, e.g. 'General/Reports') or item_id. Omitting both lists the drive root. Returns children with id, name, size, folder/file facet, and webUrl.",
			Annotations = new ToolAnnotations("List a SharePoint folder", ReadOnlyHint: true),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["drive_id"] = ToolHelpers.Prop("string", "Drive id from list_drives."),
				["item_path"] = ToolHelpers.Prop("string", "Folder path relative to the drive root. Use '/' or omit for the root."),
				["item_id"] = ToolHelpers.Prop("string", "Folder item id (alternative to item_path)."),
				["top"] = ToolHelpers.Prop("integer", "Page size (1-200). Default 50."),
				["skip_token"] = ToolHelpers.Prop("string", "Continuation token from a prior @odata.nextLink to fetch the next page.")
			}, "drive_id"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				return ToolHelpers.ContentResult(await graph.ListFolderAsync(ToolHelpers.RequireString(args, "drive_id"), ToolHelpers.OptString(args, "item_path"), ToolHelpers.OptString(args, "item_id"), ToolHelpers.OptInt(args, "top"), ToolHelpers.OptString(args, "skip_token"), ct));
			}
		};
	}
}
