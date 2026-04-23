using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Storage;

namespace ServiceNowHandoff.ServiceNow;

/// <summary>
/// Sends messages to ServiceNow and manages user ConversationReference storage
/// for proactive messaging back to the user.
/// </summary>
public class ServiceNowMessageSender
{
    private readonly IServiceNowConnectionSettings _settings;
    private readonly ServiceNowTokenProvider _tokenProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStorage _storage;
    private readonly ILogger<ServiceNowMessageSender> _logger;

    private const string UserRefKeyPrefix = "user_ref_";

    public ServiceNowMessageSender(
        IServiceNowConnectionSettings settings,
        ServiceNowTokenProvider tokenProvider,
        IHttpClientFactory httpClientFactory,
        IStorage storage,
        ILogger<ServiceNowMessageSender> logger)
    {
        _settings = settings;
        _tokenProvider = tokenProvider;
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Store the user's ConversationReference so the webhook handler can send proactive messages.
    /// </summary>
    public async Task StoreUserChannelReferenceAsync(IActivity activity, string mcsConversationId)
    {
        var reference = activity.GetConversationReference();
        var key = $"{UserRefKeyPrefix}{mcsConversationId}";

        var data = new Dictionary<string, object>
        {
            [key] = reference
        };
        await _storage.WriteAsync(data);

        _logger.LogInformation("Stored user ConversationReference for MCS conversation {McsId}", mcsConversationId);
    }

    /// <summary>
    /// Retrieve the stored ConversationReference for proactive messaging.
    /// </summary>
    public async Task<ConversationReference?> GetUserChannelReferenceAsync(string mcsConversationId)
    {
        var key = $"{UserRefKeyPrefix}{mcsConversationId}";
        var data = await _storage.ReadAsync([key]);

        if (data.TryGetValue(key, out var val) && val is ConversationReference reference)
        {
            return reference;
        }

        return null;
    }

    /// <summary>
    /// Create a new conversation in ServiceNow for live agent routing.
    /// Returns the ServiceNow conversation/sys_id.
    /// </summary>
    public async Task<string?> CreateConversationAsync(
        string mcsConversationId,
        string summary,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync(cancellationToken);
            var client = _httpClientFactory.CreateClient("ServiceNow");

            // TODO: SERVICENOW API — CREATE INTERACTION
            // This uses the ServiceNow Table API to create an interaction record.
            // Reference: ServiceNow Interact Data Instance API (Yokohama+)
            //   https://www.servicenow.com/docs/r/yokohama/api-reference/developer-guides/mobsdk-and-interact_data_instance.html
            //
            // VERIFY with your ServiceNow admin:
            //   1. The 'interaction' table is enabled and accessible via REST API
            //   2. The OAuth app has read/write access to the 'interaction' table
            //   3. The 'queue' field value matches a valid assignment group sys_id
            //   4. The 'channel' field accepts 'chat' as a value
            //   5. The 'correlation_id' field exists (custom field) or use 'correlation_display' instead
            //   6. Your ServiceNow instance version is Yokohama or later
            //
            // ALTERNATIVE: If using ServiceNow CSM (Customer Service Management),
            // you may need the CSM Chat API instead:
            //   POST /api/now/csm/messaging/v1/conversations
            //   POST /api/sn_csm_ws/cs_messaging/messages
            var url = $"{_settings.InstanceUrl.TrimEnd('/')}/api/now/table/interaction";

            var payload = new
            {
                channel = "chat",
                opened_for = mcsConversationId,
                short_description = summary,
                queue = _settings.QueueId,
                state = "new",
                // External reference for webhook callback mapping
                correlation_id = mcsConversationId
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("ServiceNow create conversation failed: {Status} {Body}",
                    response.StatusCode, errorBody);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseBody);

            var sysId = doc.RootElement
                .GetProperty("result")
                .GetProperty("sys_id")
                .GetString();

            _logger.LogInformation("Created ServiceNow interaction {SysId}", sysId);
            return sysId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating ServiceNow conversation");
            return null;
        }
    }

    /// <summary>
    /// Send a customer message to ServiceNow during an active escalation.
    /// </summary>
    public async Task SendMessageAsync(
        IActivity activity,
        string snConversationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync(cancellationToken);
            var client = _httpClientFactory.CreateClient("ServiceNow");

            // TODO: SERVICENOW API — SEND MESSAGE
            // This posts a message record to the sys_cs_message table.
            // VERIFY with your ServiceNow admin:
            //   1. The 'sys_cs_message' table exists (part of CSM/Virtual Agent Chat)
            //   2. The 'group' field maps to the interaction sys_id
            //   3. The 'role' field accepts 'end_user' (may also be 'snc_external')
            //   4. Required fields match your ServiceNow configuration
            //
            // ALTERNATIVE: If sys_cs_message is not available, try:
            //   POST /api/now/table/live_message  (Live Feed messaging)
            //   POST /api/sn_csm_ws/cs_messaging/messages  (CSM Chat)
            //   Or use a Scripted REST API endpoint created by your ServiceNow admin
            var url = $"{_settings.InstanceUrl.TrimEnd('/')}/api/now/table/sys_cs_message";

            var payload = new
            {
                group = snConversationId,
                message = activity.Text ?? string.Empty,
                role = "end_user",
                timestamp = DateTime.UtcNow.ToString("o")
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("ServiceNow send message failed: {Status} {Body}",
                    response.StatusCode, errorBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to ServiceNow");
        }
    }

    /// <summary>
    /// Notify ServiceNow that the customer ended the chat.
    /// </summary>
    public async Task EndConversationAsync(string snConversationId, CancellationToken cancellationToken)
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync(cancellationToken);
            var client = _httpClientFactory.CreateClient("ServiceNow");

            var url = $"{_settings.InstanceUrl.TrimEnd('/')}/api/now/table/interaction/{snConversationId}";

            var payload = new { state = "closed_complete" };

            var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            await client.SendAsync(request, cancellationToken);

            _logger.LogInformation("Closed ServiceNow conversation {SnId}", snConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing ServiceNow conversation {SnId}", snConversationId);
        }
    }
}
