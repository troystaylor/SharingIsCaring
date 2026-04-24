using Microsoft.Agents.Builder;
using Microsoft.Agents.CopilotStudio.Client;

namespace ServiceNowHandoff.Services;

/// <summary>
/// Creates CopilotClient instances for communicating with Copilot Studio via Direct Connect.
/// </summary>
public class CopilotClientFactory
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public CopilotClientFactory(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    public CopilotClient CreateClient(ITurnContext turnContext)
    {
        var settings = new ConnectionSettings();
        _configuration.GetSection("CopilotStudioClientSettings").Bind(settings);

        var logger = _loggerFactory.CreateLogger<CopilotClient>();

        return new CopilotClient(
            settings,
            _httpClientFactory,
            logger);
    }
}
