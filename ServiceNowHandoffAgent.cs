using System.Text.Json;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Storage;
using ServiceNowHandoff.ServiceNow;
using ServiceNowHandoff.Services;

namespace ServiceNowHandoff;

/// <summary>
/// Main agent that routes conversations between Copilot Studio and ServiceNow Live Agent.
/// Pre-handoff: messages go to Copilot Studio via Direct Line.
/// During handoff: messages are relayed to ServiceNow live agent.
/// Post-handoff: agent disconnect detected, state cleared, Copilot Studio resumes.
/// </summary>
public class ServiceNowHandoffAgent : AgentApplication
{
    private readonly DirectLineCopilotService _copilotService;
    private readonly ConversationStateManager _stateManager;
    private readonly ServiceNowMessageSender _messageSender;
    private readonly ConversationMappingStore _mappingStore;
    private readonly IServiceNowConnectionSettings _snSettings;
    private readonly IStorage _storage;
    private readonly ILogger<ServiceNowHandoffAgent> _logger;

    private const string AgentDisconnectedKeyPrefix = "agent_disconnected_";
    private const string EndChatText = "End chat with agent";

    public ServiceNowHandoffAgent(
        AgentApplicationOptions options,
        DirectLineCopilotService copilotService,
        ConversationStateManager stateManager,
        ServiceNowMessageSender messageSender,
        ConversationMappingStore mappingStore,
        IServiceNowConnectionSettings snSettings,
        IStorage storage,
        ILogger<ServiceNowHandoffAgent> logger) : base(options)
    {
        _copilotService = copilotService;
        _stateManager = stateManager;
        _messageSender = messageSender;
        _mappingStore = mappingStore;
        _snSettings = snSettings;
        _storage = storage;
        _logger = logger;

        OnActivity(ActivityTypes.Message, HandleAllActivitiesAsync);
        OnActivity(ActivityTypes.ConversationUpdate, HandleConversationUpdateAsync);
    }

    private async Task HandleAllActivitiesAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        CancellationToken cancellationToken)
    {
        var mcsConversationId = _stateManager.GetMCSConversationId(turnState);
        var isEscalated = _stateManager.GetIsEscalated(turnState);

        if (isEscalated)
        {
            // Check if live agent has disconnected
            if (await CheckAndClearAgentDisconnectedAsync(mcsConversationId, turnContext, turnState, cancellationToken))
            {
                // Agent disconnected — start fresh Copilot Studio session
                await HandleNewConversationAsync(turnContext, turnState, cancellationToken);
                return;
            }

            // Check for end-chat action from user
            if (string.Equals(turnContext.Activity.Text?.Trim(), EndChatText, StringComparison.OrdinalIgnoreCase))
            {
                await HandleEndChatAsync(turnContext, turnState, mcsConversationId, cancellationToken);
                return;
            }

            // Route user message to ServiceNow live agent
            await RouteToServiceNowAsync(turnContext, turnState, mcsConversationId, cancellationToken);
        }
        else
        {
            // Route to Copilot Studio via Direct Line
            var mcsId = _stateManager.GetMCSConversationId(turnState);
            if (!string.IsNullOrEmpty(mcsId))
            {
                await HandleCopilotStudioMessageAsync(turnContext, turnState, mcsId, cancellationToken);
            }
            else
            {
                await HandleNewConversationAsync(turnContext, turnState, cancellationToken);
            }
        }
    }

    private async Task HandleConversationUpdateAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        CancellationToken cancellationToken)
    {
        if (turnContext.Activity.MembersAdded != null)
        {
            foreach (var member in turnContext.Activity.MembersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await HandleNewConversationAsync(turnContext, turnState, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Start a new Copilot Studio conversation via Direct Line.
    /// </summary>
    private async Task HandleNewConversationAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting new Copilot Studio conversation via Direct Line");

        var (conversationId, activities) = await _copilotService.StartConversationAsync(cancellationToken);

        _stateManager.SetMCSConversationId(turnState, conversationId);

        // If user sent a message, forward it and let the response include any greeting
        if (turnContext.Activity.Type == ActivityTypes.Message
            && !string.IsNullOrEmpty(turnContext.Activity.Text))
        {
            await HandleCopilotStudioMessageAsync(turnContext, turnState,
                conversationId, cancellationToken);
        }
        else
        {
            // No user message (e.g., ConversationUpdate) — send greeting if present
            foreach (var activity in activities)
            {
                if (activity.Type == "message" && !string.IsNullOrEmpty(activity.Text)
                    && activity.From?.Role == "bot")
                {
                    await turnContext.SendActivityAsync(
                        MessageFactory.Text(activity.Text),
                        cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Send a user message to Copilot Studio via Direct Line and relay the response.
    /// Detects handoff.initiate event to trigger escalation.
    /// </summary>
    private async Task HandleCopilotStudioMessageAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        string mcsConversationId,
        CancellationToken cancellationToken)
    {
        var activities = await _copilotService.SendMessageAsync(
            mcsConversationId,
            turnContext.Activity.From.Id,
            turnContext.Activity.From.Name ?? "User",
            turnContext.Activity.Text,
            cancellationToken);

        foreach (var activity in activities)
        {
            if (activity.Type == "message" && !string.IsNullOrEmpty(activity.Text))
            {
                await turnContext.SendActivityAsync(
                    MessageFactory.Text(activity.Text),
                    cancellationToken);
            }
            else if (activity.Type == "event"
                     && string.Equals(activity.Name, "handoff.initiate", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Handoff event detected — escalating to ServiceNow");
                await HandleEscalationAsync(turnContext, turnState, activity, mcsConversationId, cancellationToken);
                return;
            }
        }
    }

    /// <summary>
    /// Handle escalation: create ServiceNow conversation, set state, notify user.
    /// </summary>
    private async Task HandleEscalationAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        DirectLineActivity handoffActivity,
        string mcsConversationId,
        CancellationToken cancellationToken)
    {
        // Extract conversation summary from handoff context
        var summary = "Customer requesting live agent assistance.";
        if (handoffActivity.Value != null)
        {
            try
            {
                var json = handoffActivity.Value is JsonElement je
                    ? je.GetRawText()
                    : JsonSerializer.Serialize(handoffActivity.Value);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var lastPhrase = root.TryGetProperty("va_LastPhrase", out var lp) ? lp.GetString() : null;
                var agentMessage = root.TryGetProperty("va_AgentMessage", out var am) ? am.GetString() : null;
                if (!string.IsNullOrEmpty(agentMessage)) summary = agentMessage;
                else if (!string.IsNullOrEmpty(lastPhrase)) summary = $"Customer said: {lastPhrase}";
            }
            catch (JsonException)
            {
                // Handoff value wasn't parseable JSON, use default summary
            }
        }

        // Notify user
        await turnContext.SendActivityAsync(
            MessageFactory.Text("Connecting you to a live agent. Please wait..."),
            cancellationToken);

        // Store the user's ConversationReference for proactive messaging
        await _messageSender.StoreUserChannelReferenceAsync(turnContext.Activity, mcsConversationId);

        // Create ServiceNow conversation
        var snConversationId = await _messageSender.CreateConversationAsync(
            mcsConversationId, summary, cancellationToken);

        if (string.IsNullOrEmpty(snConversationId))
        {
            _logger.LogError("Failed to create ServiceNow conversation");
            await turnContext.SendActivityAsync(
                MessageFactory.Text("Sorry, I wasn't able to connect you to a live agent. Please try again."),
                cancellationToken);
            return;
        }

        // Store conversation mapping
        await _mappingStore.AddMappingAsync(snConversationId, mcsConversationId);

        // Update state
        _stateManager.SetIsEscalated(turnState, true);
        _stateManager.SetServiceNowConversationId(turnState, snConversationId);

        _logger.LogInformation(
            "Escalation complete. ServiceNow conversation: {SnConversationId}, MCS: {McsConversationId}",
            snConversationId, mcsConversationId);
    }

    /// <summary>
    /// Route user message to ServiceNow during escalation.
    /// </summary>
    private async Task RouteToServiceNowAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        string? mcsConversationId,
        CancellationToken cancellationToken)
    {
        var snConversationId = _stateManager.GetServiceNowConversationId(turnState);
        if (string.IsNullOrEmpty(snConversationId))
        {
            _logger.LogWarning("Escalated but no ServiceNow conversation ID found");
            return;
        }

        await _messageSender.SendMessageAsync(
            turnContext.Activity,
            snConversationId,
            cancellationToken);
    }

    /// <summary>
    /// User chose to end the live agent chat.
    /// </summary>
    private async Task HandleEndChatAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        string? mcsConversationId,
        CancellationToken cancellationToken)
    {
        var snConversationId = _stateManager.GetServiceNowConversationId(turnState);

        // Notify ServiceNow that the customer ended the chat
        if (!string.IsNullOrEmpty(snConversationId))
        {
            await _messageSender.EndConversationAsync(snConversationId, cancellationToken);
            await _mappingStore.RemoveMappingByServiceNowIdAsync(snConversationId);
        }

        ClearConversationState(turnState);

        await turnContext.SendActivityAsync(
            MessageFactory.Text("You've been disconnected from the live agent. How else can I help you?"),
            cancellationToken);

        // Start fresh Copilot Studio session
        await HandleNewConversationAsync(turnContext, turnState, cancellationToken);
    }

    /// <summary>
    /// Check if the ServiceNow agent has disconnected (flag set by polling service).
    /// </summary>
    private async Task<bool> CheckAndClearAgentDisconnectedAsync(
        string? mcsConversationId,
        ITurnContext turnContext,
        ITurnState turnState,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(mcsConversationId)) return false;

        var key = $"{AgentDisconnectedKeyPrefix}{mcsConversationId}";
        var data = await _storage.ReadAsync([key], cancellationToken);

        if (data.ContainsKey(key))
        {
            await _storage.DeleteAsync([key], cancellationToken);
            ClearConversationState(turnState);

            await turnContext.SendActivityAsync(
                MessageFactory.Text("The live agent has left the conversation. How else can I help you?"),
                cancellationToken);

            return true;
        }

        return false;
    }

    private void ClearConversationState(ITurnState turnState)
    {
        _stateManager.SetIsEscalated(turnState, false);
        _stateManager.SetServiceNowConversationId(turnState, null);
        _stateManager.SetMCSConversationId(turnState, null);
        _stateManager.SetLastCopilotStudioReference(turnState, null);
    }
}
