namespace ServiceNowHandoff.ServiceNow;

public interface IServiceNowConnectionSettings
{
    string InstanceUrl { get; }
    string ClientId { get; }
    string ClientSecret { get; }
    string Username { get; }
    string Password { get; }
    string WebhookSecret { get; }
    string QueueId { get; }
    int PollingIntervalSeconds { get; }
}
