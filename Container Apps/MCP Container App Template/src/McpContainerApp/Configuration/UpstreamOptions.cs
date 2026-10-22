namespace McpContainerApp.Configuration;

/// <summary>The API this MCP server fronts. Replace with your own options type.</summary>
public sealed class UpstreamOptions
{
    public const string SectionName = "Upstream";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Comes from a Key Vault reference in Azure. Never put a real value in appsettings.json.</summary>
    public string ClientSecret { get; set; } = string.Empty;
}
