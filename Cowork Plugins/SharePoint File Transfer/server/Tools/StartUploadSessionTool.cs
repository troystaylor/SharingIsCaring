using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class StartUploadSessionTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "start_upload_session",
			Description = "Create a Graph upload session for a destination path and return the pre-signed uploadUrl. Caller PUTs the bytes directly (no Authorization header, chunks in multiples of 320 KiB, 5-10 MiB sweet spot). Also returns a session_token the caller can pass back to get_upload_status / cancel_upload.",
			Annotations = new ToolAnnotations("Start SharePoint upload session"),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["drive_id"] = ToolHelpers.Prop("string", "Drive id from list_drives."),
				["item_path"] = ToolHelpers.Prop("string", "Destination path relative to drive root, including file name."),
				["size_bytes"] = ToolHelpers.Prop("integer", "Total file size in bytes. Stored alongside the session for resume tracking."),
				["conflict_behavior"] = ToolHelpers.Prop("string", "rename | replace | fail. Default 'rename'."),
				["description"] = ToolHelpers.Prop("string", "Optional description applied to the resulting drive item.")
			}, "drive_id", "item_path"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				IUploadSessionStore store = sp.GetRequiredService<IUploadSessionStore>();
				string driveId = ToolHelpers.RequireString(args, "drive_id");
				string itemPath = ToolHelpers.RequireString(args, "item_path");
				JsonObject session = await graph.CreateUploadSessionAsync(driveId, itemPath, ToolHelpers.OptString(args, "conflict_behavior") ?? "rename", ToolHelpers.OptString(args, "description"), ct);
				string uploadUrl = session["uploadUrl"]?.GetValue<string>() ?? string.Empty;
				string expirationStr = session["expirationDateTime"]?.GetValue<string>();
				DateTimeOffset parsed;
				DateTimeOffset expiration = (DateTimeOffset.TryParse(expirationStr, out parsed) ? parsed : DateTimeOffset.UtcNow.AddHours(1.0));
				string token = Guid.NewGuid().ToString("N");
				long sizeBytes = ToolHelpers.OptLong(args, "size_bytes").GetValueOrDefault();
				await store.SaveAsync(new UploadSessionRecord(token, uploadUrl, driveId, itemPath, sizeBytes, expiration, DateTimeOffset.UtcNow, "Created", 0L), ct);
				JsonObject result = new JsonObject
				{
					["session_token"] = token,
					["upload_url"] = uploadUrl,
					["expiration_date_time"] = expirationStr,
					["recommended_chunk_size_bytes"] = 10485760,
					["chunk_alignment_bytes"] = 327680,
					["max_chunk_size_bytes"] = 62914560,
					["graph_response"] = session.DeepClone()
				};
				return ToolHelpers.ContentResult(result);
			}
		};
	}
}
