namespace DurableAgentWorkflows.Shared;

/// <summary>
/// Request sent to a human approver when a workflow reaches an approval gate.
/// Used by all three workflows with their respective HITL RequestPorts.
/// </summary>
public sealed record ApprovalRequest(
    string WorkflowName,
    string Summary,
    string Details,
    string RequestedAction);

/// <summary>
/// Human response submitted via the HITL respond endpoint.
/// </summary>
public sealed record ApprovalResponse(
    bool Approved,
    string Comments);

/// <summary>
/// Helper to keep agent outputs within DurableTask CustomStatus limits (16KB UTF-16).
/// </summary>
public static class TextHelpers
{
    private const int MaxSectionLength = 50;
    private const int MaxDetailsLength = 200;

    public static string Truncate(string text, int maxLength = MaxSectionLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text[..maxLength] + "\n[truncated]";
    }

    public static string TruncateDetails(string text)
        => Truncate(text, MaxDetailsLength);
}
