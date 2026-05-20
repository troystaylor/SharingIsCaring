using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlackCoworkMcp.Slack;

/// <summary>One Slack Web API method as the capability index sees it.</summary>
public sealed class SlackCapability
{
    [JsonPropertyName("method")] public string Method { get; init; } = "";
    [JsonPropertyName("domain")] public string Domain { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("required_scopes")] public string[] RequiredScopes { get; init; } = [];
    [JsonPropertyName("keywords")] public string[] Keywords { get; init; } = [];
}

/// <summary>
/// In-memory index of Slack Web API methods used by <c>scan_slack</c> and
/// <c>launch_slack</c>. Backing data lives in <c>Slack/CapabilityIndex.json</c>
/// embedded into the assembly.
///
/// Scoring is a BM25-ish lexical scorer over the tokenised query against each
/// entry's keywords, method name, description, and domain. Good enough for
/// natural-language intent → method ranking without pulling in an embedding
/// model.
/// </summary>
public sealed class SlackCapabilityIndex
{
    private readonly IReadOnlyList<SlackCapability> _entries;
    private readonly Dictionary<string, SlackCapability> _byMethod;
    private readonly Dictionary<string, double> _idf;
    private readonly double _avgDocLen;

    private SlackCapabilityIndex(IReadOnlyList<SlackCapability> entries)
    {
        _entries = entries;
        _byMethod = entries.ToDictionary(e => e.Method, StringComparer.OrdinalIgnoreCase);

        // Pre-compute IDF and avg doc length over the corpus
        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lens = new int[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var toks = Tokenise(DocText(entries[i])).Distinct(StringComparer.OrdinalIgnoreCase);
            lens[i] = Tokenise(DocText(entries[i])).Count();
            foreach (var t in toks)
            {
                docFreq[t] = docFreq.GetValueOrDefault(t) + 1;
            }
        }
        _idf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var N = (double)entries.Count;
        foreach (var (term, df) in docFreq)
        {
            _idf[term] = Math.Log(1 + (N - df + 0.5) / (df + 0.5));
        }
        _avgDocLen = lens.Length == 0 ? 1 : lens.Average();
    }

    public IReadOnlyList<SlackCapability> All => _entries;

    public bool TryGet(string method, out SlackCapability cap)
        => _byMethod.TryGetValue(method, out cap!);

    public bool Contains(string method) => _byMethod.ContainsKey(method);

    public IReadOnlyList<(SlackCapability Capability, double Score)> Search(string query, int topK = 10)
    {
        var qtoks = Tokenise(query).ToArray();
        if (qtoks.Length == 0) return Array.Empty<(SlackCapability, double)>();

        const double k1 = 1.5, b = 0.75;
        var scored = new List<(SlackCapability, double)>(_entries.Count);
        foreach (var e in _entries)
        {
            var docTerms = Tokenise(DocText(e)).ToArray();
            var dl = docTerms.Length;
            var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in docTerms) tf[t] = tf.GetValueOrDefault(t) + 1;

            double score = 0;
            foreach (var q in qtoks)
            {
                if (!tf.TryGetValue(q, out var f)) continue;
                var idf = _idf.GetValueOrDefault(q, 0);
                var denom = f + k1 * (1 - b + b * (dl / _avgDocLen));
                score += idf * ((f * (k1 + 1)) / denom);
            }

            // Boost for direct method-name match
            if (qtoks.Any(q => e.Method.Contains(q, StringComparison.OrdinalIgnoreCase)))
                score += 3.0;

            if (score > 0)
                scored.Add((e, score));
        }

        return scored.OrderByDescending(x => x.Item2).Take(topK).ToList();
    }

    private static string DocText(SlackCapability c)
        => string.Join(' ', new[] { c.Method, c.Domain, c.Description }.Concat(c.Keywords));

    private static IEnumerable<string> Tokenise(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) yield break;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    public static SlackCapabilityIndex LoadEmbedded()
    {
        var asm = typeof(SlackCapabilityIndex).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("CapabilityIndex.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("CapabilityIndex.json not found as embedded resource");
        using var s = asm.GetManifestResourceStream(resourceName)!;
        var entries = JsonSerializer.Deserialize<List<SlackCapability>>(s, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? [];
        return new SlackCapabilityIndex(entries);
    }
}
