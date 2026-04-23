using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;

namespace ServiceNowHandoff.Fallback;

/// <summary>
/// Background service managing Direct Line WebSocket connections (Option 3 fallback).
/// Handles lifecycle: connection pooling, token refresh, reconnection.
/// Use when Direct Connect (CopilotClient) is unavailable.
/// </summary>
public class DirectLineRelayService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DirectLineRelayService> _logger;

    private readonly ConcurrentDictionary<string, DirectLineStreamingClient> _activeConnections = new();
    private string? _directLineSecret;
    private string? _directLineToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public DirectLineRelayService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<DirectLineRelayService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _directLineSecret = _configuration["DirectLine:Secret"];

        if (string.IsNullOrEmpty(_directLineSecret))
        {
            _logger.LogWarning("Direct Line secret not configured; fallback relay disabled");
            return;
        }

        _logger.LogInformation("Direct Line relay fallback service started");

        // Keep the service alive — connections are managed on-demand
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshTokenIfNeededAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }

    /// <summary>
    /// Start a Direct Line WebSocket connection for a conversation.
    /// Returns the Direct Line conversation ID.
    /// </summary>
    public async Task<string> StartStreamingConnectionAsync(
        string mcsConversationId,
        ConversationReference userReference,
        Func<IActivity, Task> onActivityReceived,
        CancellationToken cancellationToken)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<DirectLineStreamingClient>>();

        var client = new DirectLineStreamingClient(
            _directLineSecret ?? throw new InvalidOperationException("Direct Line secret not configured"),
            _serviceProvider,
            logger);

        var dlConversationId = await client.StartConversationAsync(
            userReference, onActivityReceived, cancellationToken);

        _activeConnections[mcsConversationId] = client;

        _logger.LogInformation(
            "Started Direct Line streaming for MCS {McsId} → DL {DlId}",
            mcsConversationId, dlConversationId);

        return dlConversationId;
    }

    /// <summary>
    /// Send a user message through an active Direct Line connection.
    /// </summary>
    public async Task SendActivityAsync(
        string mcsConversationId, IActivity activity, CancellationToken cancellationToken)
    {
        if (_activeConnections.TryGetValue(mcsConversationId, out var client))
        {
            await client.SendActivityAsync(activity, cancellationToken);
        }
        else
        {
            _logger.LogWarning("No active Direct Line connection for MCS {McsId}", mcsConversationId);
        }
    }

    /// <summary>
    /// Stop and clean up a Direct Line connection.
    /// </summary>
    public async Task StopConnectionAsync(string mcsConversationId)
    {
        if (_activeConnections.TryRemove(mcsConversationId, out var client))
        {
            await client.DisposeAsync();
            _logger.LogInformation("Stopped Direct Line streaming for MCS {McsId}", mcsConversationId);
        }
    }

    /// <summary>
    /// Refresh the Direct Line token before expiry.
    /// </summary>
    private async Task RefreshTokenIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_directLineToken != null && DateTime.UtcNow.AddMinutes(5) < _tokenExpiry)
            return;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_directLineToken != null && DateTime.UtcNow.AddMinutes(5) < _tokenExpiry)
                return;

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_directLineSecret}");

            var response = await httpClient.PostAsync(
                "https://directline.botframework.com/v3/directline/tokens/generate",
                null,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                _directLineToken = doc.RootElement.GetProperty("token").GetString();
                _tokenExpiry = DateTime.UtcNow.AddMinutes(25); // DL tokens last ~30min
                _logger.LogInformation("Direct Line token refreshed");
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var kvp in _activeConnections)
        {
            await kvp.Value.DisposeAsync();
        }
        _activeConnections.Clear();

        await base.StopAsync(cancellationToken);
    }
}
