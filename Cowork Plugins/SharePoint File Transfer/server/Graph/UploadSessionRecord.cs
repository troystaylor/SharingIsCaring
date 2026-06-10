using System;

namespace SharePointTransferMcp.Graph;

public sealed record UploadSessionRecord(string SessionToken, string UploadUrl, string DriveId, string ItemPath, long SizeBytes, DateTimeOffset ExpirationDateTime, DateTimeOffset CreatedAt, string Status = "Created", long UploadedBytes = 0L, string? SourceUrl = null, string? ContentType = null, string? LastError = null, string? DriveItemJson = null, DateTimeOffset? CompletedAt = null);
