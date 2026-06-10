using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class CheckCopyStatusTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "check_copy_status",
			Description = "Check the status of an asynchronous copy operation started by copy_item. Pass the monitor_url returned by copy_item. Returns percentageComplete and status (notStarted | inProgress | completed | failed).",
			Annotations = new ToolAnnotations("Check copy operation status", ReadOnlyHint: true),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["monitor_url"] = ToolHelpers.Prop("string", "Monitor URL returned by copy_item.")
			}, "monitor_url"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				string monitorUrl = ToolHelpers.RequireString(args, "monitor_url");

				JsonObject status = await graph.CheckCopyStatusAsync(monitorUrl, ct);
				return ToolHelpers.ContentResult(status);
			}
		};
	}
}
