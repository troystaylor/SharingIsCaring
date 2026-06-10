using System;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class UploadFromUrlTool
{
	private const long AsyncThresholdBytes = 262144000L;

	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "upload_from_url",
			Description = "Server-side ingest: HEAD-probes an HTTPS source URL, opens a Graph upload session, and streams the bytes into SharePoint. Files at or above 250 MB run in a background worker by default so the call returns immediately with a session_token; poll get_upload_status to watch progress and pick up the resulting driveItem on completion. Smaller files run inline and return the driveItem directly. Pass async=true to force background mode (or async=false to force inline). SharePoint per-file ceiling is 250 GB. The source URL must be reachable from the MCP server without additional auth (use signed URLs) and should support HEAD or Range requests so transfers can be resumed via resume_upload_from_url if they fail.",
			Annotations = new ToolAnnotations("Upload file to SharePoint from a URL"),
			InputSchema = ToolHelpers.Schema(new JsonObject
			{
				["drive_id"] = ToolHelpers.Prop("string", "Drive id from list_drives."),
				["item_path"] = ToolHelpers.Prop("string", "Destination path relative to drive root, including file name (e.g. 'Reports/2025/Q4.pdf')."),
				["source_url"] = ToolHelpers.Prop("string", "HTTPS URL to download. Must be reachable from the MCP server without additional auth (use signed URLs)."),
				["conflict_behavior"] = ToolHelpers.Prop("string", "rename | replace | fail. Default 'rename'."),
				["async"] = ToolHelpers.Prop("boolean", "Force background execution (true) or inline execution (false). If omitted, files >= 250 MB run async and smaller files run inline.")
			}, "drive_id", "item_path", "source_url"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				IUploadSessionStore store = sp.GetRequiredService<IUploadSessionStore>();
				ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
				IHostApplicationLifetime lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
				ILogger log = loggerFactory.CreateLogger("UploadFromUrl");
				string driveId = ToolHelpers.RequireString(args, "drive_id");
				string itemPath = ToolHelpers.RequireString(args, "item_path");
				string source = ToolHelpers.RequireString(args, "source_url");
				string conflictBehavior = ToolHelpers.OptString(args, "conflict_behavior") ?? "rename";
				JsonNode jsonNode = args["async"];
				bool b;
				bool? asyncOverride = ((jsonNode is JsonValue v && v.TryGetValue<bool>(out b)) ? new bool?(b) : ((bool?)null));
				if (!Uri.TryCreate(source, UriKind.Absolute, out Uri uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
				{
					throw new ArgumentException("source_url must be an absolute http(s) URL");
				}
				SourceUrlProbe probe = await graph.ProbeSourceUrlAsync(source, ct);
				if (probe.ContentLength <= 0)
				{
					throw new ArgumentException("source URL did not return Content-Length; cannot upload to Graph upload session");
				}
				JsonObject session = await graph.CreateUploadSessionAsync(driveId, itemPath, conflictBehavior, null, ct);
				string uploadUrl = session["uploadUrl"]?.GetValue<string>() ?? throw new InvalidOperationException("createUploadSession returned no uploadUrl");
				string expirationStr = session["expirationDateTime"]?.GetValue<string>();
				DateTimeOffset parsed;
				DateTimeOffset expiration = (DateTimeOffset.TryParse(expirationStr, out parsed) ? parsed : DateTimeOffset.UtcNow.AddHours(1.0));
				string token = Guid.NewGuid().ToString("N");
				await store.SaveAsync(new UploadSessionRecord(token, uploadUrl, driveId, itemPath, probe.ContentLength, expiration, DateTimeOffset.UtcNow, "Running", 0L, source, probe.ContentType), ct);
				if (!(asyncOverride ?? (probe.ContentLength >= 262144000)))
				{
					try
					{
						JsonObject driveItem = await graph.RunSessionFromUrlAsync(uploadUrl, source, probe.ContentLength, 0L, new Progress<long>(delegate(long uploaded)
						{
							store.UpdateProgressAsync(token, uploaded, CancellationToken.None);
						}), ct);
						await store.UpdateStatusAsync(token, "Completed", null, driveItem.ToJsonString(), CancellationToken.None);
						return ToolHelpers.ContentResult(new JsonObject
						{
							["session_token"] = token,
							["status"] = "completed",
							["mode"] = "sync",
							["total_bytes"] = probe.ContentLength,
							["content_type"] = probe.ContentType,
							["drive_item"] = driveItem.DeepClone()
						});
					}
					catch (Exception ex)
					{
						Exception ex2 = ex;
						await store.UpdateStatusAsync(token, "Failed", ex2.Message, null, CancellationToken.None);
						try
						{
							await graph.CancelUploadAtUrlAsync(uploadUrl, CancellationToken.None);
						}
						catch
						{
						}
						if (!(ex is Exception source2))
						{
							throw ex;
						}
						ExceptionDispatchInfo.Capture(source2).Throw();
					}
				}
				CancellationToken backgroundCt = lifetime.ApplicationStopping;
				Task.Run(async delegate
				{
					try
					{
						log.LogInformation("background upload starting: token={Token} bytes={Bytes} path={Path}", token, probe.ContentLength, itemPath);
						JsonObject driveItem2 = await graph.RunSessionFromUrlAsync(uploadUrl, source, probe.ContentLength, 0L, new Progress<long>(delegate(long uploaded)
						{
							store.UpdateProgressAsync(token, uploaded, CancellationToken.None);
						}), backgroundCt);
						await store.UpdateStatusAsync(token, "Completed", null, driveItem2.ToJsonString(), CancellationToken.None);
						log.LogInformation("background upload completed: token={Token}", token);
					}
					catch (Exception ex3)
					{
						log.LogError(ex3, "background upload failed: token={Token}", token);
						await store.UpdateStatusAsync(token, "Failed", ex3.Message, null, CancellationToken.None);
					}
				}, CancellationToken.None);
				return ToolHelpers.ContentResult(new JsonObject
				{
					["session_token"] = token,
					["status"] = "running",
					["mode"] = "async",
					["upload_url"] = uploadUrl,
					["total_bytes"] = probe.ContentLength,
					["content_type"] = probe.ContentType,
					["supports_range_resume"] = probe.SupportsRange,
					["expiration_date_time"] = expirationStr,
					["next_step"] = "poll get_upload_status with this session_token until status is 'Completed' or 'Failed'"
				});
			}
		};
	}
}
