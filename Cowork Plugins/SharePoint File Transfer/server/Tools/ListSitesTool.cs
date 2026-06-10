using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class ListSitesTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "list_sites",
			Description = "Search SharePoint sites the signed-in user can access. Use this when the user names a team or library by name and you need a site id before listing drives.",
			Annotations = new ToolAnnotations("List SharePoint sites", ReadOnlyHint: true),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["query"] = ToolHelpers.Prop("string", "Free-text search (matches site title and URL). Omit or pass '*' to list all visible sites."),
				["top"] = ToolHelpers.Prop("integer", "Page size (1-100). Default 25.")
			}),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				return ToolHelpers.ContentResult(await graph.SearchSitesAsync(ToolHelpers.OptString(args, "query"), ToolHelpers.OptInt(args, "top"), ct));
			}
		};
	}
}
