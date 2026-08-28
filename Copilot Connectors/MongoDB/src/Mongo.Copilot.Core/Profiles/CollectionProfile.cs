namespace Mongo.Copilot.Core.Profiles;

/// <summary>
/// Determines which head (or heads) a collection is exposed through.
/// </summary>
public enum ProfileMode
{
    /// <summary>Crawled into the Microsoft 365 search index by the indexer.</summary>
    Indexed,

    /// <summary>Queried in real time by the federated MCP server.</summary>
    Live,

    /// <summary>Both indexed for discovery and queryable live for detail.</summary>
    Both
}

/// <summary>
/// The subset of Microsoft Graph external item property types this template supports.
/// Graph external item schemas are flat and strongly typed, so MongoDB's nested
/// documents must be projected down to these.
/// </summary>
public enum PropType
{
    String,
    Int64,
    Double,
    DateTime,
    Boolean,
    StringCollection
}

/// <summary>
/// How a document's audience is determined. The same mapping is enforced as an
/// index-time ACL by the indexer and as a query-time filter by the federated server.
/// </summary>
public enum AccessKind
{
    /// <summary>Grant to all users in the tenant. A governance decision, never a default.</summary>
    Everyone,

    /// <summary>A document field holds an Entra group object ID.</summary>
    EntraGroupField,

    /// <summary>A document field holds a user principal name.</summary>
    UpnField,

    /// <summary>A document field is matched against a claim on the caller's token.</summary>
    TenantField
}

/// <summary>
/// The access rule for a collection. <see cref="Field"/> is required for every kind
/// except <see cref="AccessKind.Everyone"/>.
/// </summary>
public sealed class AccessMapping
{
    public AccessKind Kind { get; init; }

    /// <summary>Document field carrying the group ID, UPN, or tenant value.</summary>
    public string? Field { get; init; }

    /// <summary>
    /// For <see cref="AccessKind.TenantField"/>, the claim on the caller's token whose
    /// value must equal <see cref="Field"/>. Defaults to the tenant ID claim.
    /// </summary>
    public string ClaimType { get; init; } = "http://schemas.microsoft.com/identity/claims/tenantid";

    public bool RequiresField => Kind != AccessKind.Everyone;
}

/// <summary>
/// One property projected out of a MongoDB document into both the Graph external item
/// schema and the MCP tool response.
/// </summary>
public sealed class PropertyProfile
{
    public PropType Type { get; init; } = PropType.String;

    /// <summary>Dotted path into the document. Defaults to the property name.</summary>
    public string? Path { get; init; }

    public bool IsSearchable { get; init; }
    public bool IsQueryable { get; init; }
    public bool IsRetrievable { get; init; } = true;
    public bool IsRefinable { get; init; }

    /// <summary>
    /// Graph semantic labels such as <c>title</c>, <c>url</c>, <c>lastModifiedDateTime</c>,
    /// or <c>createdBy</c>. These materially affect grounding and result display.
    /// </summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}

/// <summary>
/// The single source of truth for how one MongoDB collection is exposed to Copilot.
/// Loaded from JSON so installers never recompile.
/// </summary>
public sealed class CollectionProfile
{
    /// <summary>Key from the profiles document; also the MongoDB collection name.</summary>
    public string Name { get; set; } = string.Empty;

    public ProfileMode Mode { get; init; } = ProfileMode.Live;

    /// <summary>Database holding the collection. Falls back to the connection default.</summary>
    public string? Database { get; init; }

    /// <summary>
    /// Business-language description. Becomes the MCP tool description Copilot reads when
    /// deciding whether to query this collection, so it should describe the data, not the schema.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    public string IdField { get; init; } = "_id";
    public string? TitleField { get; init; }
    public string? ContentField { get; init; }

    /// <summary>Relative path template resolved against installer-configured base URLs.</summary>
    public string? UrlPath { get; init; }

    /// <summary>
    /// Opt into sharing a Graph connection with other profiles. When null, the collection
    /// gets its own connection and schema.
    /// </summary>
    public string? ConnectionGroup { get; init; }

    public IReadOnlyDictionary<string, PropertyProfile> Properties { get; init; }
        = new Dictionary<string, PropertyProfile>();

    /// <summary>Required. A profile without an access rule is a startup error.</summary>
    public AccessMapping? Access { get; init; }

    public bool IsIndexed => Mode is ProfileMode.Indexed or ProfileMode.Both;
    public bool IsLive => Mode is ProfileMode.Live or ProfileMode.Both;

    /// <summary>Connection this profile belongs to: its group, or itself.</summary>
    public string ConnectionKey => ConnectionGroup ?? Name;

    /// <summary>Document path for a property, honouring an explicit override.</summary>
    public string PathFor(string propertyName) =>
        Properties.TryGetValue(propertyName, out var p) && !string.IsNullOrWhiteSpace(p.Path)
            ? p.Path!
            : propertyName;

    /// <summary>Every document field this profile reads, for projection pushdown.</summary>
    public IEnumerable<string> ReferencedFields()
    {
        yield return IdField;
        if (!string.IsNullOrWhiteSpace(TitleField)) yield return TitleField!;
        if (!string.IsNullOrWhiteSpace(ContentField)) yield return ContentField!;
        if (Access?.Field is { Length: > 0 } accessField) yield return accessField;

        foreach (var name in Properties.Keys)
            yield return PathFor(name);

        foreach (var token in UrlTokens())
            yield return token;
    }

    /// <summary>Field names referenced by <see cref="UrlPath"/> placeholders.</summary>
    public IEnumerable<string> UrlTokens() => UrlTemplateTokens.Extract(UrlPath);
}
