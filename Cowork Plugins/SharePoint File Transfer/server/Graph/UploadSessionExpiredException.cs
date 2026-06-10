using System;

namespace SharePointTransferMcp.Graph;

public sealed class UploadSessionExpiredException : Exception
{
	public string UploadUrl { get; }

	public UploadSessionExpiredException(string uploadUrl, string message)
		: base(message)
	{
		UploadUrl = uploadUrl;
	}
}
