using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;

namespace ServiceNowHandoff.ServiceNow;

/// <summary>
/// Handles inbound webhooks from ServiceNow when a live agent sends a reply.
/// Uses proactive messaging (ProcessProactiveAsync) to push the agent's reply
/// to the user immediately — WITHOUT requiring the user to send another message.
/// This is the core fix for the polling problem.
/// </summary>
public class ServiceNowWebhookHandler
{
    private readonly ServiceNowMessageSender _messageSender;
    private readonly ConversationMappingStore _mappingStore;
    private readonly IServiceNowConnectionSettings _settings;
    private readonly ILogger<ServiceNowWebhookHandler> _logger;

    public ServiceNowWebhookHandler(
        ServiceNowMessageSender messageSender,
        ConversationMappingStore mappingStore,
        IServiceNowConnectionSettings settings,
        ILogger<ServiceNowWebhookHandler> logger)
    {
        _messageSender = messageSender;
        _mappingStore = mappingStore;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Process an incoming webhook from ServiceNow containing an agent reply.
    /// </summary>
    public async Task<IResult> HandleAsync(HttpRequest request, IServiceProvider serviceProvider)
    {
        // Read the raw body
        using var reader = new StreamReader(request.Body);
        var requestBody = await reader.ReadToEndAsync();

        // Validate webhook authenticity
        if (!ValidateWebhookSignature(request, requestBody))
        {
            _logger.LogWarning("ServiceNow webhook signature validation failed");
            return Results.Unauthorized();
        }

        // Parse the webhook payload
        ServiceNowWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ServiceNowWebhookPayload>(requestBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse ServiceNow webhook payload");
            return Results.BadRequest("Invalid payload");
        }

        if (payload == null || string.IsNullOrEmpty(payload.Message))
        {
            return Results.Ok(); // Empty payload, nothing to do
        }

        _logger.LogInformation(
            "Received agent reply from ServiceNow. ConversationId: {SnId}, Agent: {AgentName}",
            payload.ConversationId, payload.AgentName);

        // Look up the MCS conversation ID from the ServiceNow conversation ID
        var mcsConversationId = await _mappingStore.GetMcsConversationIdAsync(payload.ConversationId);
        if (string.IsNullOrEmpty(mcsConversationId))
        {
            _logger.LogWarning(
                "No MCS mapping found for ServiceNow conversation {SnId}", payload.ConversationId);
            return Results.NotFound();
        }

        // Get the stored user ConversationReference
        var userRef = await _messageSender.GetUserChannelReferenceAsync(mcsConversationId);
        if (userRef == null)
        {
            _logger.LogWarning(
                "No user ConversationReference found for MCS conversation {McsId}", mcsConversationId);
            return Results.NotFound();
        }

        // Build the reply activity
        var replyActivity = MessageFactory.Text(payload.Message);

        // Add agent name prefix if available
        if (!string.IsNullOrEmpty(payload.AgentName))
        {
            replyActivity.Text = $"**{payload.AgentName}**: {payload.Message}";
        }

        // Add "End chat with agent" suggested action
        replyActivity.SuggestedActions = new SuggestedActions
        {
            Actions = new List<CardAction>
            {
                new CardAction
                {
                    Title = "End chat with agent",
                    Type = ActionTypes.ImBack,
                    Value = "End chat with agent"
                }
            }
        };

        // Send proactively to user — this is the key pattern from GenesysHandoff
        var channelAdapter = serviceProvider.GetRequiredService<IChannelAdapter>();
        await SendProactiveActivityAsync(channelAdapter, userRef, replyActivity);

        _logger.LogInformation("Proactively sent agent reply to user for MCS conversation {McsId}", mcsConversationId);

        return Results.Ok();
    }

    /// <summary>
    /// Send an activity to the user proactively using the stored ConversationReference.
    /// This uses ProcessProactiveAsync to push messages without requiring user input.
    /// </summary>
    private static async Task SendProactiveActivityAsync(
        IChannelAdapter channelAdapter,
        ConversationReference userChannelReference,
        IActivity activity)
    {
        var continuationActivity = userChannelReference.GetContinuationActivity();
        var agentAppId = userChannelReference.Agent?.Id ?? string.Empty;
        var claimsIdentity = new ClaimsIdentity(
            new List<Claim>
            {
                new("aud", agentAppId),
                new("appid", agentAppId),
            });

        await channelAdapter.ProcessProactiveAsync(
            claimsIdentity: claimsIdentity,
            continuationActivity: continuationActivity,
            audience: string.Empty,
            callback: async (turnContext, ct) =>
            {
                await turnContext.SendActivityAsync(activity, cancellationToken: ct);
            },
            cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Validate the ServiceNow webhook signature using HMAC-SHA256.
    /// </summary>
    private bool ValidateWebhookSignature(HttpRequest request, string requestBody)
    {
        if (string.IsNullOrEmpty(_settings.WebhookSecret))
        {
            // No secret configured — skip validation (dev only)
            _logger.LogWarning("WebhookSecret not configured; skipping signature validation");
            return true;
        }

        if (!request.Headers.TryGetValue("X-ServiceNow-Signature", out var signatureHeader)
            || string.IsNullOrEmpty(signatureHeader))
        {
            // Also check Authorization header for API key pattern
            if (request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                return string.Equals(authHeader, $"Bearer {_settings.WebhookSecret}", StringComparison.Ordinal);
            }
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(requestBody));
        var computedSignature = Convert.ToBase64String(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signatureHeader!));
    }
}

/// <summary>
/// Represents the inbound webhook payload from ServiceNow when an agent sends a message.
///
/// TODO: SERVICENOW WEBHOOK CONFIGURATION
/// You must configure ServiceNow to POST to this endpoint when a live agent sends a reply.
/// Options (pick one):
///
///   1. BUSINESS RULE: Create an "after insert" Business Rule on the sys_cs_message table
///      (or live_message) that fires when role='agent'. The script should call:
///        var r = new sn_ws.RESTMessageV2();
///        r.setEndpoint('https://YOUR-APP.azurewebsites.net/api/servicenow/webhook');
///        r.setHttpMethod('POST');
///        r.setRequestHeader('X-ServiceNow-Signature', 'YOUR-HMAC-SIGNATURE');
///        r.setRequestBody(JSON.stringify({
///            ConversationId: current.group.toString(),
///            Message: current.message.toString(),
///            AgentName: current.sys_created_by.toString(),
///            EventType: 'agent_message',
///            Timestamp: current.sys_created_on.toString()
///        }));
///        r.execute();
///
///   2. FLOW DESIGNER: Create a Flow triggered on record creation in sys_cs_message
///      with a REST step that posts to this endpoint.
///
///   3. SCRIPTED REST API + OUTBOUND REST: Configure a Notification or Event
///      that triggers an Outbound REST Message to this endpoint.
///
/// The payload fields below MUST match what your ServiceNow script sends.
/// Adjust the property names if your ServiceNow admin uses different field names.
/// </summary>
public class ServiceNowWebhookPayload
{
    public string ConversationId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? AgentName { get; set; }
    public string? AgentId { get; set; }
    public string? EventType { get; set; }
    public DateTime? Timestamp { get; set; }
}
