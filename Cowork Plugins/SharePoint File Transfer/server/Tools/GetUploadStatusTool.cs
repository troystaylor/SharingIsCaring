using System;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class GetUploadStatusTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "get_upload_status",
			Description = "Check the status of an in-flight or completed upload. For sessions created by upload_from_url, returns the persisted status (Running | Completed | Failed | Expired), uploaded_bytes, total_bytes, last_error (on failure), and drive_item (on success). For sessions created by start_upload_session (caller-driven chunking), queries Graph for nextExpectedRanges. Pass either session_token or upload_url.",
			Annotations = new ToolAnnotations("Get upload session status", ReadOnlyHint: true),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["session_token"] = ToolHelpers.Prop("string", "Token returned by upload_from_url or start_upload_session."),
				["upload_url"] = ToolHelpers.Prop("string", "Pre-signed upload URL (alternative to session_token; required when no record was persisted).")
			}),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				IUploadSessionStore store = sp.GetRequiredService<IUploadSessionStore>();
				string url = ToolHelpers.OptString(args, "upload_url");
				string token = ToolHelpers.OptString(args, "session_token");
				UploadSessionRecord record = null;
				if (!string.IsNullOrEmpty(token))
				{
					record = await store.GetAsync(token, ct);
					if ((object)record == null && string.IsNullOrEmpty(url))
					{
						throw new ArgumentException("no stored session for token " + token + "; pass upload_url directly");
					}
					if ((object)record != null && string.IsNullOrEmpty(url))
					{
						url = record.UploadUrl;
					}
				}
				else if (string.IsNullOrEmpty(url))
				{
					throw new ArgumentException("must supply session_token or upload_url");
				}
				JsonObject result = new JsonObject
				{
					["upload_url"] = url,
					["session_token"] = token
				};
				if ((object)record != null)
				{
					result["status"] = record.Status;
					result["uploaded_bytes"] = record.UploadedBytes;
					result["total_bytes"] = record.SizeBytes;
					result["drive_id"] = record.DriveId;
					result["item_path"] = record.ItemPath;
					result["expiration_date_time"] = record.ExpirationDateTime.ToString("O");
					if (!string.IsNullOrEmpty(record.SourceUrl))
					{
						result["source_url"] = record.SourceUrl;
					}
					if (!string.IsNullOrEmpty(record.ContentType))
					{
						result["content_type"] = record.ContentType;
					}
					if (!string.IsNullOrEmpty(record.LastError))
					{
						result["last_error"] = record.LastError;
					}
					if (record.CompletedAt.HasValue)
					{
						result["completed_at"] = record.CompletedAt.Value.ToString("O");
					}
					if (record.SizeBytes > 0)
					{
						double pct = (double)record.UploadedBytes / (double)record.SizeBytes * 100.0;
						result["percent_complete"] = Math.Round(pct, 2);
					}
					if (!string.IsNullOrEmpty(record.DriveItemJson))
					{
						try
						{
							result["drive_item"] = JsonNode.Parse(record.DriveItemJson);
						}
						catch
						{
							result["drive_item_raw"] = record.DriveItemJson;
						}
					}
				}
				if (((object)record == null || (!string.Equals(record.Status, "Completed", StringComparison.OrdinalIgnoreCase) && !string.Equals(record.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) && !string.Equals(record.Status, "Expired", StringComparison.OrdinalIgnoreCase))) && !string.IsNullOrEmpty(url))
				{
					try
					{
						result["graph_response"] = (await graph.GetUploadStatusFromUrlAsync(url, ct)).DeepClone();
					}
					catch (GraphApiException ex) when (ex.StatusCode == 404)
					{
						result["graph_response"] = new JsonObject
						{
							["status"] = 404,
							["note"] = "upload session no longer exists on Graph (404); it has expired or been completed"
						};
						if ((object)record != null && !string.Equals(record.Status, "Completed", StringComparison.OrdinalIgnoreCase))
						{
							await store.UpdateStatusAsync(token, "Expired", "Graph upload session no longer exists (HTTP 404)", null, CancellationToken.None);
							result["status"] = "Expired";
						}
					}
				}
				return ToolHelpers.ContentResult(result);
			}
		};
	}
}
