using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;

namespace ServiceNowHandoff.Fallback;

/// <summary>
/// Direct Line WebSocket streaming client (Option 3 fallback).
/// Maintains a persistent WebSocket connection to Direct Line so that activities
/// (including live agent replies) are received in real-time and pushed to the user
/// proactively — without requiring the user to send another message.
///
/// Use this when Direct Connect (CopilotClient) is not available.
/// </summary>
public class DirectLineStreamingClient : IAsyncDisposable
{
    private readonly string _directLineSecret;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DirectLineStreamingClient> _logger;

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _listenerCts;
    private string? _conversationId;
    private string? _streamUrl;
    private ConversationReference? _userReference;
    private Func<IActivity, Task>? _onActivityReceived;

    private const string DirectLineBaseUrl = "https://directline.botframework.com/v3/directline";

    public DirectLineStreamingClient(
        string directLineSecret,
        IServiceProvider serviceProvider,
        ILogger<DirectLineStreamingClient> logger)
    {
        _directLineSecret = directLineSecret;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Start a new Direct Line conversation and open the WebSocket stream.
    /// </summary>
    public async Task<string> StartConversationAsync(
        ConversationReference userReference,
        Func<IActivity, Task> onActivityReceived,
        CancellationToken cancellationToken)
    {
        _userReference = userReference;
        _onActivityReceived = onActivityReceived;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_directLineSecret}");

        var response = await httpClient.PostAsync(
            $"{DirectLineBaseUrl}/conversations",
            null,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        _conversationId = doc.RootElement.GetProperty("conversationId").GetString()!;
        _streamUrl = doc.RootElement.GetProperty("streamUrl").GetString()!;

        _logger.LogInformation("Direct Line conversation started: {ConversationId}", _conversationId);

        // Start WebSocket listener in background
        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ListenAsync(_listenerCts.Token), _listenerCts.Token);

        return _conversationId;
    }

    /// <summary>
    /// Send an activity (user message) to the Direct Line conversation.
    /// </summary>
    public async Task SendActivityAsync(IActivity activity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_conversationId))
            throw new InvalidOperationException("Conversation not started");

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_directLineSecret}");

        var url = $"{DirectLineBaseUrl}/conversations/{_conversationId}/activities";

        var content = new StringContent(
            JsonSerializer.Serialize(activity),
            Encoding.UTF8,
            "application/json");

        await httpClient.PostAsync(url, content, cancellationToken);
    }

    /// <summary>
    /// Persistent WebSocket listener that receives activities in real-time.
    /// </summary>
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(_streamUrl!), cancellationToken);

                _logger.LogInformation("Direct Line WebSocket connected for {ConversationId}", _conversationId);

                var buffer = new byte[8192];

                while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var messageBuilder = new StringBuilder();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _webSocket.ReceiveAsync(buffer, cancellationToken);
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("Direct Line WebSocket closed by server");
                        break;
                    }

                    var messageText = messageBuilder.ToString();
                    if (string.IsNullOrWhiteSpace(messageText)) continue;

                    await ProcessWebSocketMessageAsync(messageText);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Direct Line WebSocket error, reconnecting in 5s...");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Process activities received from the Direct Line WebSocket.
    /// Forward bot messages to the user proactively.
    /// </summary>
    private async Task ProcessWebSocketMessageAsync(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);

            if (!doc.RootElement.TryGetProperty("activities", out var activitiesElement))
                return;

            foreach (var actElement in activitiesElement.EnumerateArray())
            {
                var type = actElement.GetProperty("type").GetString();
                var fromRole = actElement.TryGetProperty("from", out var from)
                    ? from.TryGetProperty("role", out var role) ? role.GetString() : null
                    : null;

                // Only forward bot/agent messages, not user messages (which we sent)
                if (type == "message" && fromRole == "bot")
                {
                    var text = actElement.TryGetProperty("text", out var textProp)
                        ? textProp.GetString() : null;

                    if (!string.IsNullOrEmpty(text) && _userReference != null && _onActivityReceived != null)
                    {
                        var replyActivity = MessageFactory.Text(text);
                        await _onActivityReceived(replyActivity);
                    }
                }
                else if (type == "event")
                {
                    var eventName = actElement.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() : null;

                    if (eventName == "handoff.initiate" && _onActivityReceived != null)
                    {
                        var eventActivity = new Activity
                        {
                            Type = ActivityTypes.Event,
                            Name = "handoff.initiate",
                            Value = actElement.TryGetProperty("value", out var valProp)
                                ? valProp.GetRawText() : null
                        };
                        await _onActivityReceived(eventActivity);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Direct Line WebSocket message");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listenerCts?.Cancel();

        if (_webSocket is { State: WebSocketState.Open })
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Disposing",
                    CancellationToken.None);
            }
            catch { /* Best effort */ }
        }

        _webSocket?.Dispose();
        _listenerCts?.Dispose();
    }
}
