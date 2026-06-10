using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class ListDrivesTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "list_drives",
			Description = "List the document libraries (drives) inside a SharePoint site. Pass the site id from list_sites. Returns each drive's id, name, driveType, and webUrl.",
			Annotations = new ToolAnnotations("List drives in a SharePoint site", ReadOnlyHint: true),
			InputSchema = ToolHelpers.Schema(new JsonObject { ["site_id"] = ToolHelpers.Prop("string", "The site id returned from list_sites (e.g. 'contoso.sharepoint.com,GUID,GUID').") }, "site_id"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				return ToolHelpers.ContentResult(await graph.ListDrivesAsync(ToolHelpers.RequireString(args, "site_id"), ct));
			}
		};
	}
}
