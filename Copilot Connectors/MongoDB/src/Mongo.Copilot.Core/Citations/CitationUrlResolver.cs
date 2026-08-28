using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Mongo.Copilot.Core.Data;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core.Citations;

/// <summary>
/// The only component that builds citation URLs. Both heads call it, so an indexed item
/// and a live tool response always cite the same location.
/// </summary>
public sealed class CitationUrlResolver(IOptions<UiOptions> options)
{
    private readonly UiOptions _ui = options.Value;

    /// <summary>
    /// Resolves the citation URL for a document, or null when the collection is uncited or
    /// a placeholder cannot be filled. A missing token yields no link rather than a broken one.
    /// </summary>
    public string? Resolve(CollectionProfile profile, BsonDocument document)
    {
        var template = _ui.TemplateFor(profile);
        if (template is null)
            return null;

        var unresolved = false;

        var url = UrlTemplateTokens.Pattern.Replace(template, match =>
        {
            var path = match.Groups[1].Value;
            var value = BsonValues.ReadPath(document, path);

            if (value is null or BsonNull)
            {
                unresolved = true;
                return string.Empty;
            }

            var text = BsonValues.ToPlainString(value);
            if (string.IsNullOrEmpty(text))
            {
                unresolved = true;
                return string.Empty;
            }

            return Uri.EscapeDataString(text);
        });

        return unresolved ? null : url;
    }

    public string Fingerprint(IEnumerable<CollectionProfile> profiles) => _ui.Fingerprint(profiles);
}
