namespace SharePointTransferMcp.Graph;

public sealed record SourceUrlProbe(long ContentLength, string? ContentType, bool SupportsRange);
