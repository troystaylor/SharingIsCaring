using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;

namespace ServiceNowHandoff.Services;

/// <summary>
/// Manages per-conversation state properties stored in ITurnState.
/// </summary>
public class ConversationStateManager
{
    private const string MCSConversationIdKey = "MCSConversationId";
    private const string IsEscalatedKey = "IsEscalated";
    private const string ServiceNowConversationIdKey = "ServiceNowConversationId";
    private const string LastCopilotStudioReferenceKey = "LastCopilotStudioReference";

    public string? GetMCSConversationId(ITurnState turnState)
        => turnState.Conversation.TryGetValue<string>(MCSConversationIdKey, out var val) ? val : null;

    public void SetMCSConversationId(ITurnState turnState, string? value)
    {
        if (value != null)
            turnState.Conversation.SetValue(MCSConversationIdKey, value);
        else
            turnState.Conversation.DeleteValue(MCSConversationIdKey);
    }

    public bool GetIsEscalated(ITurnState turnState)
        => turnState.Conversation.TryGetValue<bool>(IsEscalatedKey, out var val) && val;

    public void SetIsEscalated(ITurnState turnState, bool value)
        => turnState.Conversation.SetValue(IsEscalatedKey, value);

    public string? GetServiceNowConversationId(ITurnState turnState)
        => turnState.Conversation.TryGetValue<string>(ServiceNowConversationIdKey, out var val) ? val : null;

    public void SetServiceNowConversationId(ITurnState turnState, string? value)
    {
        if (value != null)
            turnState.Conversation.SetValue(ServiceNowConversationIdKey, value);
        else
            turnState.Conversation.DeleteValue(ServiceNowConversationIdKey);
    }

    public ConversationReference? GetLastCopilotStudioReference(ITurnState turnState)
        => turnState.Conversation.TryGetValue<ConversationReference>(LastCopilotStudioReferenceKey, out var val)
            ? val : null;

    public void SetLastCopilotStudioReference(ITurnState turnState, ConversationReference? value)
    {
        if (value != null)
            turnState.Conversation.SetValue(LastCopilotStudioReferenceKey, value);
        else
            turnState.Conversation.DeleteValue(LastCopilotStudioReferenceKey);
    }
}
