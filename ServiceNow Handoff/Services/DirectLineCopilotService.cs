using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceNowHandoff.Services;

/// <summary>
/// Communicates with Copilot Studio via Direct Line REST API.
/// Used instead of CopilotClient because S2S (server-to-server) is not yet
/// supported by the Agents SDK CopilotClient. Direct Line uses a secret key
/// and does not require user-interactive sign-in.
///
/// Get the Direct Line secret from Copilot Studio:
///   Settings > Security > Web channel security > Copy secret
/// </summary>
public class DirectLineCopilotService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DirectLineCopilotService> _logger;

    // Store per-conversation tokens (Direct Line returns a token when starting a conversation)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _conversationTokens = new();

    // Store per-conversation watermarks to avoid returning old activities
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _conversationWatermarks = new();

    private const string DirectLineBaseUrl = "https://directline.botframework.com/v3/directline";

    public DirectLineCopilotService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DirectLineCopilotService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private string GetSecret() =>
        _configuration["DirectLine:Secret"]
        ?? throw new InvalidOperationException(
            "DirectLine:Secret is not configured. Get it from Copilot Studio > Settings > Security > Web channel security.");

    private string GetAuthToken(string conversationId) =>
        _conversationTokens.TryGetValue(conversationId, out var token) ? token : GetSecret();

    /// <summary>
    /// Start a new Direct Line conversation with Copilot Studio.
    /// Returns (conversationId, initial bot activities).
    /// </summary>
    public async Task<(string ConversationId, List<DirectLineActivity> Activities)> StartConversationAsync(
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("DirectLine");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GetSecret());

        var response = await client.PostAsync(
            $"{DirectLineBaseUrl}/conversations",
            null,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var conversationId = doc.RootElement.GetProperty("conversationId").GetString()!;

        // Store the conversation token for subsequent requests
        if (doc.RootElement.TryGetProperty("token", out var tokenProp))
        {
            var token = tokenProp.GetString();
            if (!string.IsNullOrEmpty(token))
            {
                _conversationTokens[conversationId] = token;
                _logger.LogInformation("Stored conversation token for {ConversationId}", conversationId);
            }
        }

        _logger.LogInformation("Started Direct Line conversation: {ConversationId}", conversationId);

        // Wait briefly for the bot's greeting message
        await Task.Delay(2000, cancellationToken);

        var (activities, watermark) = await GetActivitiesAsync(conversationId, null, cancellationToken);
        if (!string.IsNullOrEmpty(watermark))
            _conversationWatermarks[conversationId] = watermark;

        return (conversationId, activities);
    }

    /// <summary>
    /// Send a user message to the Copilot Studio agent via Direct Line
    /// and return the bot's response activities.
    /// </summary>
    public async Task<List<DirectLineActivity>> SendMessageAsync(
        string conversationId,
        string userId,
        string userName,
        string text,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("DirectLine");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GetAuthToken(conversationId));

        var payload = new
        {
            type = "message",
            from = new { id = userId, name = userName },
            text
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(
            $"{DirectLineBaseUrl}/conversations/{conversationId}/activities",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        // Read the response to get the activity ID
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);
        var sentActivityId = doc.RootElement.GetProperty("id").GetString();

        _logger.LogInformation("Sent message to Direct Line, activity ID: {ActivityId}", sentActivityId);

        // Poll for bot response (use stored watermark to skip already-seen activities)
        _conversationWatermarks.TryGetValue(conversationId, out var currentWatermark);
        return await PollForBotResponseAsync(conversationId, userId, currentWatermark, cancellationToken);
    }

    /// <summary>
    /// Poll Direct Line for bot response activities after sending a user message.
    /// </summary>
    private async Task<List<DirectLineActivity>> PollForBotResponseAsync(
        string conversationId,
        string userId,
        string? initialWatermark,
        CancellationToken cancellationToken,
        int maxAttempts = 10,
        int delayMs = 1500)
    {
        string? watermark = initialWatermark;
        var botActivities = new List<DirectLineActivity>();

        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(delayMs, cancellationToken);

            var (activities, newWatermark) = await GetActivitiesAsync(conversationId, watermark, cancellationToken);
            if (!string.IsNullOrEmpty(newWatermark))
            {
                watermark = newWatermark;
                _conversationWatermarks[conversationId] = newWatermark;
            }

            foreach (var activity in activities)
            {
                // Only include bot responses, not the user's own messages
                if (activity.From?.Id != userId)
                {
                    botActivities.Add(activity);
                }
            }

            // If we got bot activities, check if we should keep waiting
            if (botActivities.Count > 0)
            {
                // Check if any are handoff events — return immediately
                if (botActivities.Any(a =>
                    a.Type == "event" &&
                    string.Equals(a.Name, "handoff.initiate", StringComparison.OrdinalIgnoreCase)))
                {
                    return botActivities;
                }

                // If the last activity is a message, wait one more poll to check for follow-ups
                if (i > 0) return botActivities;
            }
        }

        return botActivities;
    }

    /// <summary>
    /// Get activities from a Direct Line conversation.
    /// </summary>
    private async Task<(List<DirectLineActivity> Activities, string? Watermark)> GetActivitiesAsync(
        string conversationId,
        string? watermark,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("DirectLine");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GetAuthToken(conversationId));

        var url = $"{DirectLineBaseUrl}/conversations/{conversationId}/activities";
        if (!string.IsNullOrEmpty(watermark))
            url += $"?watermark={watermark}";

        var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Direct Line GetActivities failed: {Status} {Body}",
                response.StatusCode, errorBody);
            return (new List<DirectLineActivity>(), null);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<DirectLineActivitySet>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return (result?.Activities ?? new List<DirectLineActivity>(), result?.Watermark);
    }

    /// <summary>
    /// Remove stored token for a conversation (cleanup).
    /// </summary>
    public void RemoveConversation(string conversationId)
    {
        _conversationTokens.TryRemove(conversationId, out _);
    }
}

public class DirectLineActivitySet
{
    public List<DirectLineActivity> Activities { get; set; } = new();
    public string? Watermark { get; set; }
}

public class DirectLineActivity
{
    public string? Type { get; set; }
    public string? Id { get; set; }
    public string? Text { get; set; }
    public string? Name { get; set; }
    public DirectLineFrom? From { get; set; }
    public object? Value { get; set; }

    [JsonPropertyName("conversation")]
    public DirectLineConversation? Conversation { get; set; }
}

public class DirectLineFrom
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Role { get; set; }
}

public class DirectLineConversation
{
    public string? Id { get; set; }
}
