namespace ServiceNowHandoff.ServiceNow;

public class ServiceNowConnectionSettings : IServiceNowConnectionSettings
{
    public string InstanceUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string QueueId { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; } = 15;
}
