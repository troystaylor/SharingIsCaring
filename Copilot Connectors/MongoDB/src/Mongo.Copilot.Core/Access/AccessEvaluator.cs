using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using Mongo.Copilot.Core.Data;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core.Access;

/// <summary>An entry in a Graph external item ACL.</summary>
/// <param name="Type">One of <c>user</c>, <c>group</c>, <c>everyone</c>, or <c>externalGroup</c>.</param>
/// <param name="Value">Entra object ID, UPN, or the literal <c>everyone</c>.</param>
/// <param name="AccessType">Either <c>grant</c> or <c>deny</c>.</param>
public readonly record struct AclEntry(string Type, string Value, string AccessType)
{
    public static AclEntry GrantEveryone() => new("everyone", "everyone", "grant");

    /// <summary>
    /// Denies everyone. Used for the reserved state item so it can never surface in search.
    /// </summary>
    public static AclEntry DenyEveryone() => new("everyone", "everyone", "deny");

    public static AclEntry GrantGroup(string objectId) => new("group", objectId, "grant");

    public static AclEntry GrantUser(string upnOrObjectId) => new("user", upnOrObjectId, "grant");
}

/// <summary>
/// Turns a profile's single <see cref="AccessMapping"/> into whichever form the calling
/// head needs: an ACL at index time, or a mandatory query filter at request time.
/// </summary>
public static class AccessEvaluator
{
    /// <summary>
    /// Builds the ACL for one document. Returns null when the rule cannot be resolved, in
    /// which case the caller must skip the document rather than publish it — a wrong grant
    /// in the search index is visible tenant-wide until a re-crawl fixes it.
    /// </summary>
    public static IReadOnlyList<AclEntry>? BuildAcl(CollectionProfile profile, BsonDocument document)
    {
        var access = profile.Access
            ?? throw new InvalidOperationException($"[{profile.Name}] Access is required.");

        if (access.Kind == AccessKind.Everyone)
            return [AclEntry.GrantEveryone()];

        var raw = BsonValues.ReadPath(document, access.Field!);
        if (raw is null || raw.IsBsonNull)
            return null;

        var values = raw.BsonType == BsonType.Array
            ? raw.AsBsonArray.Where(v => !v.IsBsonNull).Select(BsonValues.ToPlainString)
            : [BsonValues.ToPlainString(raw)];

        var entries = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => access.Kind switch
            {
                AccessKind.EntraGroupField => AclEntry.GrantGroup(v),
                AccessKind.UpnField => AclEntry.GrantUser(v),

                // A tenant value is a scoping key, not an identity. It cannot be expressed
                // as an index-time ACL, so grouped tenants must be indexed per tenant.
                _ => AclEntry.GrantEveryone()
            })
            .ToList();

        return entries.Count > 0 ? entries : null;
    }

    /// <summary>
    /// Whether this access kind can be represented as an index-time ACL at all.
    /// </summary>
    public static bool IsIndexable(AccessMapping access) => access.Kind != AccessKind.TenantField;

    /// <summary>
    /// The mandatory filter injected before every live query. Returns a filter that matches
    /// nothing when the caller's token lacks the required claim, so a missing claim yields
    /// an empty result rather than an unfiltered one.
    /// </summary>
    public static FilterDefinition<BsonDocument> BuildFilter(
        CollectionProfile profile,
        ClaimsPrincipal? user) => BuildFilterDocument(profile, user);

    /// <summary>
    /// The same rule as a raw <see cref="BsonDocument"/>, for callers that splice it into an
    /// aggregation pipeline. This is the single implementation; <see cref="BuildFilter"/>
    /// relies on the implicit conversion, so the two can never diverge.
    /// </summary>
    public static BsonDocument BuildFilterDocument(CollectionProfile profile, ClaimsPrincipal? user)
    {
        var access = profile.Access
            ?? throw new InvalidOperationException($"[{profile.Name}] Access is required.");

        switch (access.Kind)
        {
            case AccessKind.Everyone:
                return [];

            case AccessKind.EntraGroupField:
            {
                var groups = CollectClaims(user, "groups", ClaimTypes.Role).ToArray();
                return groups.Length == 0
                    ? MatchNothing()
                    : new BsonDocument(access.Field,
                        new BsonDocument("$in", new BsonArray(groups)));
            }

            case AccessKind.UpnField:
            {
                var upn = FirstClaim(user, "preferred_username", ClaimTypes.Upn, ClaimTypes.Name, "email");
                return upn is null ? MatchNothing() : new BsonDocument(access.Field, upn);
            }

            case AccessKind.TenantField:
            {
                var tenant = FirstClaim(user, access.ClaimType, "tid");
                return tenant is null ? MatchNothing() : new BsonDocument(access.Field, tenant);
            }

            default:
                return MatchNothing();
        }
    }

    /// <summary>
    /// A filter guaranteed to match no documents. Used wherever authorization cannot be
    /// established, so the failure mode is "no results" rather than "all results".
    /// </summary>
    private static BsonDocument MatchNothing() =>
        new("_id", new BsonDocument("$exists", false));

    private static IEnumerable<string> CollectClaims(ClaimsPrincipal? user, params string[] types)
    {
        if (user is null)
            return [];

        return types
            .SelectMany(t => user.FindAll(t))
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? FirstClaim(ClaimsPrincipal? user, params string[] types) =>
        CollectClaims(user, types).FirstOrDefault();
}
