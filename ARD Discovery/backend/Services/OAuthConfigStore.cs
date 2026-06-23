#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ArdDiscovery.Services;

/// <summary>
/// Per-domain OAuth configuration. In production, store in Key Vault or App Configuration.
/// For the prototype, loaded from environment variable "OAuthConfigs" as JSON.
/// </summary>
public class OAuthConfig
{
    public string Domain { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthorizeUrl { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;

    /// <summary>
    /// If set, this domain supports Entra OBO exchange with this scope
    /// (e.g., "api://target-app-id/.default"). The proxy tries OBO first.
    /// </summary>
    public string OboScope { get; set; } = string.Empty;
}

/// <summary>
/// Stores OAuth configurations per target domain.
/// Admin pre-registers these for known services.
/// </summary>
public class OAuthConfigStore
{
    private readonly Dictionary<string, OAuthConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public OAuthConfigStore()
    {
        LoadFromEnvironment();
    }

    /// <summary>
    /// Get OAuth config for a target domain. Returns null if not registered.
    /// </summary>
    public OAuthConfig? GetConfig(string targetDomain)
    {
        return _configs.TryGetValue(targetDomain, out var config) ? config : null;
    }

    /// <summary>
    /// Check if a target domain has OAuth configured (i.e., requires auth).
    /// </summary>
    public bool RequiresAuth(string targetDomain)
    {
        return _configs.ContainsKey(targetDomain);
    }

    /// <summary>
    /// Extract the domain from a URL for config lookup.
    /// </summary>
    public static string ExtractDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }
        return url;
    }

    /// <summary>
    /// Load configs from OAuthConfigs environment variable.
    /// Format: JSON array of OAuthConfig objects.
    /// Example:
    /// [{"Domain":"api.acme.com","ClientId":"...","ClientSecret":"...","AuthorizeUrl":"...","TokenUrl":"...","Scopes":"..."}]
    /// </summary>
    private void LoadFromEnvironment()
    {
        var configJson = Environment.GetEnvironmentVariable("OAuthConfigs");
        if (string.IsNullOrEmpty(configJson)) return;

        try
        {
            var configs = JsonSerializer.Deserialize<List<OAuthConfig>>(configJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (configs == null) return;

            foreach (var config in configs)
            {
                if (!string.IsNullOrEmpty(config.Domain))
                {
                    _configs[config.Domain] = config;
                }
            }
        }
        catch
        {
            // Invalid config — skip silently
        }
    }
}
