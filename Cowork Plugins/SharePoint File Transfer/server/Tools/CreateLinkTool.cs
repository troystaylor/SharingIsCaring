using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class CreateLinkTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "create_link",
			Description = "Create a shareable link for a SharePoint item. type: 'view' | 'edit' | 'embed'. scope: 'anonymous' | 'organization' | 'users'. Optional password and expirationDateTime (ISO-8601, UTC).",
			Annotations = new ToolAnnotations("Create SharePoint share link"),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["drive_id"] = ToolHelpers.Prop("string", "Drive id."),
				["item_id"] = ToolHelpers.Prop("string", "Item id to share."),
				["type"] = ToolHelpers.Prop("string", "view | edit | embed. Default 'view'."),
				["scope"] = ToolHelpers.Prop("string", "anonymous | organization | users. Default 'organization'."),
				["password"] = ToolHelpers.Prop("string", "Optional password (only honoured for 'anonymous' scope)."),
				["expiration_date_time"] = ToolHelpers.Prop("string", "Optional ISO-8601 UTC expiry, e.g. '2025-12-31T23:59:59Z'.")
			}, "drive_id", "item_id"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				DateTimeOffset? expiry = null;
				string raw = ToolHelpers.OptString(args, "expiration_date_time");
				if (!string.IsNullOrWhiteSpace(raw))
				{
					if (!DateTimeOffset.TryParse(raw, out var parsed))
					{
						throw new ArgumentException("expiration_date_time must be ISO-8601 UTC");
					}
					expiry = parsed;
				}
				return ToolHelpers.ContentResult(await graph.CreateShareLinkAsync(ToolHelpers.RequireString(args, "drive_id"), ToolHelpers.RequireString(args, "item_id"), ToolHelpers.OptString(args, "type") ?? "view", ToolHelpers.OptString(args, "scope") ?? "organization", ToolHelpers.OptString(args, "password"), expiry, ct));
			}
		};
	}
}
