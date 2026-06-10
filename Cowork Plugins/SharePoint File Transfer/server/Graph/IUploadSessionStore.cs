using System.Threading;
using System.Threading.Tasks;

namespace SharePointTransferMcp.Graph;

public interface IUploadSessionStore
{
	Task SaveAsync(UploadSessionRecord record, CancellationToken ct);

	Task<UploadSessionRecord?> GetAsync(string sessionToken, CancellationToken ct);

	Task DeleteAsync(string sessionToken, CancellationToken ct);

	/// <summary>Persist progress for an in-flight upload. Best-effort; failures are logged and swallowed.</summary>
	Task UpdateProgressAsync(string sessionToken, long uploadedBytes, CancellationToken ct);

	/// <summary>Persist terminal state (Completed | Failed | Cancelled). Best-effort; failures are logged and swallowed.</summary>
	Task UpdateStatusAsync(string sessionToken, string status, string? lastError, string? driveItemJson, CancellationToken ct);
}
