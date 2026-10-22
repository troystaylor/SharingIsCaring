using System.Text.RegularExpressions;

namespace SalesforceMcp.Configuration;

/// <summary>
/// Which Salesforce org the server talks to, and how. The server holds no Salesforce credential of its own:
/// each caller signs in to Salesforce and the server uses their access token, so every call runs with that
/// person's own permissions.
/// </summary>
public sealed partial class SalesforceOptions
{
    public const string SectionName = "Salesforce";

    /// <summary>
    /// Your My Domain URL, for example <c>https://acme.my.salesforce.com</c>. Callers' tokens are checked
    /// against, and used with, this org only. A token from any other org is refused by Salesforce.
    /// </summary>
    public string InstanceUrl { get; set; } = string.Empty;

    /// <summary>REST API version used when a call does not ask for one, for example <c>v66.0</c>.</summary>
    public string ApiVersion { get; set; } = "v66.0";

    /// <summary>
    /// How long a cached object description is served without asking Salesforce. After this, the cache
    /// revalidates with If-Modified-Since, so a description that has not changed costs a 304 and no payload.
    /// 0 revalidates on every call.
    /// </summary>
    public int SchemaCacheSeconds { get; set; } = 300;

    /// <summary>Most object descriptions held in memory per replica, across all users. The oldest entry is dropped first.</summary>
    public int SchemaCacheMaxEntries { get; set; } = 200;

    /// <summary>
    /// Registers the tools that read Salesforce setup metadata through the Tooling API: formulas, field
    /// descriptions and help text, validation rules. The signed-in user still needs permission to see them.
    /// </summary>
    public bool EnableToolingTools { get; set; } = true;

    /// <summary>
    /// Registers the create, update, and delete tools. Off by default so a fresh deployment is read-only.
    /// The signed-in user's Salesforce permissions still apply either way.
    /// </summary>
    public bool EnableWriteTools { get; set; }

    /// <summary>Most records one tool call returns, whatever the caller asks for.</summary>
    public int MaxRecords { get; set; } = 200;

    /// <summary>Most characters of record data one tool call returns, to protect the model's context window.</summary>
    public int MaxResponseChars { get; set; } = 100_000;

    public Uri InstanceUri => new(InstanceUrl.TrimEnd('/') + "/");

    public void Validate()
    {
        if (!Uri.TryCreate(InstanceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Salesforce:InstanceUrl must be an https My Domain URL, for example https://acme.my.salesforce.com.");
        }

        if (!string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')))
        {
            throw new InvalidOperationException("Salesforce:InstanceUrl must be the bare domain, with no path.");
        }

        if (!ApiVersionPattern().IsMatch(ApiVersion))
        {
            throw new InvalidOperationException("Salesforce:ApiVersion must look like v66.0.");
        }

        if (MaxRecords is < 1 or > 2000)
        {
            throw new InvalidOperationException("Salesforce:MaxRecords must be between 1 and 2000.");
        }

        if (MaxResponseChars is < 5_000 or > 1_000_000)
        {
            throw new InvalidOperationException("Salesforce:MaxResponseChars must be between 5000 and 1000000.");
        }

        if (SchemaCacheSeconds is < 0 or > 86_400)
        {
            throw new InvalidOperationException("Salesforce:SchemaCacheSeconds must be between 0 and 86400.");
        }

        if (SchemaCacheMaxEntries is < 1 or > 500)
        {
            throw new InvalidOperationException("Salesforce:SchemaCacheMaxEntries must be between 1 and 500.");
        }
    }

    [GeneratedRegex(@"^v\d{2,3}\.0$")]
    private static partial Regex ApiVersionPattern();
}
