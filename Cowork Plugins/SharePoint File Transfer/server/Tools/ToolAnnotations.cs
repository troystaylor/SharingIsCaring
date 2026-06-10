namespace SharePointTransferMcp.Tools;

public sealed record ToolAnnotations(string? Title = null, bool ReadOnlyHint = false, bool DestructiveHint = false, bool IdempotentHint = false, bool OpenWorldHint = true);
