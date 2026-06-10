using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class GetItemTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "get_item",
			Description = "Get a drive item's metadata (id, name, size, parentReference, webUrl, etag). Pass either item_path or item_id.",
			Annotations = new ToolAnnotations("Get SharePoint item", ReadOnlyHint: true),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["drive_id"] = ToolHelpers.Prop("string", "Drive id from list_drives."),
				["item_path"] = ToolHelpers.Prop("string", "Item path relative to drive root (e.g. 'General/Q4 plan.docx')."),
				["item_id"] = ToolHelpers.Prop("string", "Item id (alternative to item_path).")
			}, "drive_id"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				string path = ToolHelpers.OptString(args, "item_path");
				string id = ToolHelpers.OptString(args, "item_id");
				if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(id))
				{
					throw new ArgumentException("must supply item_path or item_id");
				}
				return ToolHelpers.ContentResult(await graph.GetItemAsync(ToolHelpers.RequireString(args, "drive_id"), path, id, ct));
			}
		};
	}
}
