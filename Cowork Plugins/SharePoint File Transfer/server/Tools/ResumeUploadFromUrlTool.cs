using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharePointTransferMcp.Graph;

namespace SharePointTransferMcp.Tools;

internal static class ResumeUploadFromUrlTool
{
	public static ToolDescriptor Build()
	{
		return new ToolDescriptor
		{
			Name = "resume_upload_from_url",
			Description = "Resume a Failed (or partially-uploaded Running) upload created by upload_from_url. Queries the Graph upload session for nextExpectedRanges, then re-fetches the source URL with a Range header starting at the next expected byte and resumes chunked PUTs in a background worker. Returns immediately with the current progress; poll get_upload_status. Requires that the source URL supports HTTP Range requests (Accept-Ranges: bytes). If the upload session has expired (404 from Graph) the only option is to call upload_from_url again with conflict_behavior='replace'.",
			Annotations = new ToolAnnotations("Resume SharePoint upload from a URL"),
			InputSchema = ToolHelpers.Schema(new JsonObject { ["session_token"] = ToolHelpers.Prop("string", "Token returned by upload_from_url.") }, "session_token"),
			Invoke = async delegate(IServiceProvider sp, JsonObject args, CancellationToken ct)
			{
				IGraphClient graph = sp.GetRequiredService<IGraphClient>();
				IUploadSessionStore store = sp.GetRequiredService<IUploadSessionStore>();
				ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
				IHostApplicationLifetime lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
				ILogger log = loggerFactory.CreateLogger("ResumeUploadFromUrl");
				string token = ToolHelpers.RequireString(args, "session_token");
				UploadSessionRecord record = (await store.GetAsync(token, ct)) ?? throw new ArgumentException("no stored session for token " + token + "; the session record was never persisted or has been deleted");
				if (string.IsNullOrEmpty(record.SourceUrl))
				{
					throw new ArgumentException("stored session has no source_url; resume_upload_from_url only works for sessions created by upload_from_url");
				}
				if (string.Equals(record.Status, "Completed", StringComparison.OrdinalIgnoreCase))
				{
					return ToolHelpers.ContentResult(new JsonObject
					{
						["session_token"] = token,
						["status"] = "completed",
						["note"] = "upload was already completed; nothing to resume",
						["uploaded_bytes"] = record.UploadedBytes,
						["total_bytes"] = record.SizeBytes
					});
				}
				long startOffset;
				try
				{
					startOffset = ParseNextExpectedStart(await graph.GetUploadStatusFromUrlAsync(record.UploadUrl, ct), record.SizeBytes);
				}
				catch (GraphApiException ex) when (ex.StatusCode == 404)
				{
					await store.UpdateStatusAsync(token, "Expired", "Graph upload session no longer exists (HTTP 404)", null, CancellationToken.None);
					throw new InvalidOperationException("upload session expired (HTTP 404 from Graph); call upload_from_url again with conflict_behavior='replace'");
				}
				if (startOffset >= record.SizeBytes)
				{
					await store.UpdateStatusAsync(token, "Completed", null, null, CancellationToken.None);
					return ToolHelpers.ContentResult(new JsonObject
					{
						["session_token"] = token,
						["status"] = "completed",
						["note"] = "Graph reports no more expected ranges; the upload had already finished",
						["uploaded_bytes"] = record.SizeBytes,
						["total_bytes"] = record.SizeBytes
					});
				}
				await store.UpdateProgressAsync(token, startOffset, CancellationToken.None);
				string uploadUrl = record.UploadUrl;
				string sourceUrl = record.SourceUrl;
				long totalBytes = record.SizeBytes;
				CancellationToken backgroundCt = lifetime.ApplicationStopping;
				Task.Run(async delegate
				{
					try
					{
						log.LogInformation("background resume starting: token={Token} offset={Offset} total={Total}", token, startOffset, totalBytes);
						JsonObject driveItem = await graph.RunSessionFromUrlAsync(uploadUrl, sourceUrl, totalBytes, startOffset, new Progress<long>(delegate(long uploaded)
						{
							store.UpdateProgressAsync(token, uploaded, CancellationToken.None);
						}), backgroundCt);
						await store.UpdateStatusAsync(token, "Completed", null, driveItem.ToJsonString(), CancellationToken.None);
						log.LogInformation("background resume completed: token={Token}", token);
					}
					catch (Exception ex2)
					{
						log.LogError(ex2, "background resume failed: token={Token}", token);
						await store.UpdateStatusAsync(token, "Failed", ex2.Message, null, CancellationToken.None);
					}
				}, CancellationToken.None);
				return ToolHelpers.ContentResult(new JsonObject
				{
					["session_token"] = token,
					["status"] = "running",
					["mode"] = "async",
					["resumed_from_byte"] = startOffset,
					["total_bytes"] = totalBytes,
					["next_step"] = "poll get_upload_status with this session_token until status is 'Completed' or 'Failed'"
				});
			}
		};
	}

	private static long ParseNextExpectedStart(JsonObject graphStatus, long totalBytes)
	{
		if (!(graphStatus["nextExpectedRanges"] is JsonArray { Count: not 0 } jsonArray))
		{
			return totalBytes;
		}
		string text = jsonArray[0]?.GetValue<string>();
		if (string.IsNullOrEmpty(text))
		{
			return 0L;
		}
		int num = text.IndexOf('-');
		string s = ((num >= 0) ? text.Substring(0, num) : text);
		long result;
		return long.TryParse(s, out result) ? result : 0;
	}
}
