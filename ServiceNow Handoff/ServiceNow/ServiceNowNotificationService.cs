using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Storage;

namespace ServiceNowHandoff.ServiceNow;

/// <summary>
/// Background service that polls ServiceNow for agent disconnect events.
/// When a live agent closes or leaves the conversation, this service detects it
/// and sends a proactive notification to the user.
/// </summary>
public class ServiceNowNotificationService : BackgroundService
{
    private readonly IServiceNowConnectionSettings _settings;
    private readonly ServiceNowTokenProvider _tokenProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConversationMappingStore _mappingStore;
    private readonly ServiceNowMessageSender _messageSender;
    private readonly IStorage _storage;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceNowNotificationService> _logger;

    private const string AgentDisconnectedKeyPrefix = "agent_disconnected_";

    public ServiceNowNotificationService(
        IServiceNowConnectionSettings settings,
        ServiceNowTokenProvider tokenProvider,
        IHttpClientFactory httpClientFactory,
        ConversationMappingStore mappingStore,
        ServiceNowMessageSender messageSender,
        IStorage storage,
        IServiceProvider serviceProvider,
        ILogger<ServiceNowNotificationService> logger)
    {
        _settings = settings;
        _tokenProvider = tokenProvider;
        _httpClientFactory = httpClientFactory;
        _mappingStore = mappingStore;
        _messageSender = messageSender;
        _storage = storage;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ServiceNow notification polling service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollActiveConversationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ServiceNow notification polling");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
        }
    }

    private async Task PollActiveConversationsAsync(CancellationToken cancellationToken)
    {
        var activeMappings = await _mappingStore.GetAllActiveMappingsAsync();

        foreach (var (snId, mcsId) in activeMappings)
        {
            try
            {
                var state = await GetConversationStateAsync(snId, cancellationToken);

                if (state is "closed_complete" or "closed_incomplete" or "closed_abandoned")
                {
                    _logger.LogInformation(
                        "ServiceNow conversation {SnId} is closed (state: {State}). Notifying user.",
                        snId, state);

                    await HandleAgentDisconnectAsync(snId, mcsId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling ServiceNow conversation {SnId}", snId);
            }
        }
    }

    /// <summary>
    /// Query ServiceNow for the current state of a conversation/interaction.
    /// </summary>
    private async Task<string?> GetConversationStateAsync(
        string snConversationId, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient("ServiceNow");

        var url = $"{_settings.InstanceUrl.TrimEnd('/')}/api/now/table/interaction/{snConversationId}?sysparm_fields=state";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);

        return doc.RootElement
            .GetProperty("result")
            .GetProperty("state")
            .GetString();
    }

    /// <summary>
    /// Handle agent disconnect: set flag for next user turn, send proactive notification.
    /// </summary>
    private async Task HandleAgentDisconnectAsync(
        string snConversationId, string mcsConversationId, CancellationToken cancellationToken)
    {
        // Set the disconnect flag so the agent clears state on next user message
        var key = $"{AgentDisconnectedKeyPrefix}{mcsConversationId}";
        await _storage.WriteAsync(new Dictionary<string, object> { [key] = true });

        // Remove the conversation mapping
        await _mappingStore.RemoveMappingByServiceNowIdAsync(snConversationId);

        // Send proactive notification to user
        var userRef = await _messageSender.GetUserChannelReferenceAsync(mcsConversationId);
        if (userRef != null)
        {
            var channelAdapter = _serviceProvider.GetRequiredService<IChannelAdapter>();
            var notification = MessageFactory.Text(
                "The live agent has left the conversation. Send a message to continue with the virtual assistant.");

            var continuationActivity = userRef.GetContinuationActivity();
            var agentAppId = userRef.Agent?.Id ?? string.Empty;
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
                    await turnContext.SendActivityAsync(notification, cancellationToken: ct);
                },
                cancellationToken: cancellationToken);

            _logger.LogInformation("Sent agent disconnect notification for MCS {McsId}", mcsConversationId);
        }
    }
}
