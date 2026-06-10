using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SharePointTransferMcp.Graph;

public interface IGraphClient
{
	Task<JsonObject> SearchSitesAsync(string? query, int? top, CancellationToken ct);

	Task<JsonObject> ListDrivesAsync(string siteId, CancellationToken ct);

	Task<JsonObject> ListFolderAsync(string driveId, string? itemPath, string? itemId, int? top, string? skipToken, CancellationToken ct);

	Task<JsonObject> GetItemAsync(string driveId, string? itemPath, string? itemId, CancellationToken ct);

	Task<JsonObject> CreateFolderAsync(string driveId, string parentPath, string name, string conflictBehavior, CancellationToken ct);

	Task<JsonObject> MoveItemAsync(string driveId, string itemId, string? newParentId, string? newName, CancellationToken ct);

	Task<JsonObject> SetListItemFieldsAsync(string driveId, string itemId, JsonObject fields, CancellationToken ct);

	Task<JsonObject> CreateShareLinkAsync(string driveId, string itemId, string type, string scope, string? password, DateTimeOffset? expirationDateTime, CancellationToken ct);

	Task<JsonObject> CreateUploadSessionAsync(string driveId, string itemPath, string conflictBehavior, string? description, CancellationToken ct);

	Task<JsonObject> GetUploadStatusFromUrlAsync(string uploadUrl, CancellationToken ct);

	Task CancelUploadAtUrlAsync(string uploadUrl, CancellationToken ct);

	/// <summary>HEAD-probes a remote URL and returns its Content-Length and Content-Type. Falls back to GET-with-Range:0-0 if HEAD is not allowed.</summary>
	Task<SourceUrlProbe> ProbeSourceUrlAsync(string sourceUrl, CancellationToken ct);

	/// <summary>Streams a remote URL into Graph via an upload session and returns the resulting driveItem.</summary>
	Task<JsonObject> UploadFromUrlAsync(string driveId, string itemPath, string sourceUrl, string conflictBehavior, CancellationToken ct);

	/// <summary>Streams a remote URL into an existing Graph upload session, optionally resuming from <paramref name="startOffset" />. Reports cumulative progress.</summary>
	Task<JsonObject> RunSessionFromUrlAsync(string uploadUrl, string sourceUrl, long totalBytes, long startOffset, IProgress<long>? progress, CancellationToken ct);

	/// <summary>Copies a driveItem to another drive/folder (cross-site supported). Returns the monitor URL for tracking progress.</summary>
	Task<string> CopyItemAsync(string driveId, string itemId, string destDriveId, string destFolderId, string? newName, string conflictBehavior, CancellationToken ct);

	/// <summary>Checks the status of an async copy operation via its monitor URL.</summary>
	Task<JsonObject> CheckCopyStatusAsync(string monitorUrl, CancellationToken ct);
}
