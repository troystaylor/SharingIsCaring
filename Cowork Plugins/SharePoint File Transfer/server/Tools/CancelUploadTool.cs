using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class CancelUploadTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "cancel_upload",
			Description = "Cancel an in-flight upload session and delete its stored record. Pass session_token or upload_url. Idempotent: 404 from Graph is treated as success.",
			Annotations = new ToolAnnotations("Cancel upload session", ReadOnlyHint: false, DestructiveHint: true, IdempotentHint: true),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["session_token"] = ToolHelpers.Prop("string", "Token returned by start_upload_session."),
				["upload_url"] = ToolHelpers.Prop("string", "Pre-signed upload URL (alternative to session_token).")
			}),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				IUploadSessionStore store = sp.GetRequiredService<IUploadSessionStore>();
				string url = ToolHelpers.OptString(args, "upload_url");
				string token = ToolHelpers.OptString(args, "session_token");
				if (string.IsNullOrEmpty(url))
				{
					if (string.IsNullOrEmpty(token))
					{
						throw new ArgumentException("must supply session_token or upload_url");
					}
					UploadSessionRecord record = await store.GetAsync(token, ct);
					if ((object)record == null)
					{
						return ToolHelpers.ContentResult(new JsonObject
						{
							["cancelled"] = true,
							["note"] = "no stored session for token; nothing to cancel",
							["session_token"] = token
						});
					}
					url = record.UploadUrl;
				}
				await graph.CancelUploadAtUrlAsync(url, ct);
				if (!string.IsNullOrEmpty(token))
				{
					await store.UpdateStatusAsync(token, "Cancelled", null, null, CancellationToken.None);
				}
				return ToolHelpers.ContentResult(new JsonObject
				{
					["cancelled"] = true,
					["session_token"] = token,
					["upload_url"] = url
				});
			}
		};
	}
}
