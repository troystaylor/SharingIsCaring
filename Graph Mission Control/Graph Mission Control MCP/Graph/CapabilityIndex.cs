using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraphMissionControl.Mcp.Graph;

/// <summary>One Microsoft Graph operation from the shared capability index.</summary>
public sealed record CapabilityEntry
{
    [JsonPropertyName("cid")] public string Cid { get; init; } = "";
    [JsonPropertyName("endpoint")] public string Endpoint { get; init; } = "";
    [JsonPropertyName("method")] public string Method { get; init; } = "GET";
    [JsonPropertyName("outcome")] public string Outcome { get; init; } = "";
    [JsonPropertyName("domain")] public string Domain { get; init; } = "";
    [JsonPropertyName("readOnly")] public bool ReadOnly { get; init; }
    [JsonPropertyName("requiredParams")] public string[] RequiredParams { get; init; } = [];
    [JsonPropertyName("optionalParams")] public string[] OptionalParams { get; init; } = [];
}

/// <summary>
/// The shared capability index, filtered to its read-only half.
///
/// The index is the same file the Power Platform connector embeds. Only the read-only
/// entries are ever loaded here, so a write operation cannot be reached even by
/// constructing a path by hand.
/// </summary>
public sealed class CapabilityIndex
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<CapabilityEntry> _readOnly;
    private readonly List<string[]> _readOnlyPatterns;
    private readonly string[] _domains;
    private readonly string _surface;

    private CapabilityIndex(IEnumerable<CapabilityEntry> entries)
    {
        // readOnly is NOT derivable from the HTTP verb — /search/query, /me/findMeetingTimes
        // and /me/calendar/getSchedule are POSTs that only read. Filter on the flag.
        _readOnly = [.. entries.Where(e => e.ReadOnly)];
        _readOnlyPatterns = [.. _readOnly.Select(e => Segments(e.Endpoint))];

        var byDomain = _readOnly
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Domain) ? "other" : e.Domain)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();

        _domains = [.. byDomain.Select(g => g.Key)];

        var sb = new StringBuilder();
        foreach (var group in byDomain)
        {
            sb.Append(group.Key).Append(": ");
            sb.AppendJoin(", ", group
                .Select(e => e.Endpoint)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal));
            sb.Append('\n');
        }
        _surface = sb.ToString().TrimEnd();
    }

    public IReadOnlyList<CapabilityEntry> ReadOnlyOperations => _readOnly;

    /// <summary>Domains present in the readable surface, in a stable order.</summary>
    public IReadOnlyList<string> Domains => _domains;

    /// <summary>
    /// Every readable path, grouped by domain. Federated connectors expose only tools, so this is
    /// the one channel an agent has for learning what it may read.
    /// </summary>
    public string DescribeReadableSurface() => _surface;

    public static CapabilityIndex LoadEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("graph-capability-index.json")
            ?? throw new InvalidOperationException("graph-capability-index.json is not embedded in the assembly");

        var entries = JsonSerializer.Deserialize<List<CapabilityEntry>>(stream, Json)
            ?? throw new InvalidOperationException("graph-capability-index.json did not parse");

        return new CapabilityIndex(entries);
    }

    /// <summary>
    /// True when the path corresponds to a read-only operation in the index.
    /// Query string is ignored; only the path shape is matched.
    /// </summary>
    public bool IsAllowedReadPath(string path)
    {
        var actual = Segments(path);
        if (actual.Length == 0) return false;

        // Traversal segments never appear in a legitimate Graph path and could be
        // normalised downstream into something the match above already approved.
        foreach (var segment in actual)
        {
            if (segment is "." or "..") return false;
        }

        foreach (var pattern in _readOnlyPatterns)
        {
            if (Matches(pattern, actual)) return true;
        }
        return false;
    }

    private static bool Matches(string[] pattern, string[] actual)
    {
        if (pattern.Length != actual.Length) return false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var p = pattern[i];
            var a = actual[i];

            var brace = p.IndexOf('{', StringComparison.Ordinal);
            if (brace < 0)
            {
                if (!string.Equals(p, a, StringComparison.OrdinalIgnoreCase)) return false;
                continue;
            }

            // Placeholder segment. A bare {id} matches any non-empty segment; a segment such
            // as search(q='{query}') must still match its literal prefix.
            if (brace == 0)
            {
                if (a.Length == 0) return false;
                continue;
            }

            if (!a.StartsWith(p[..brace], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static string[] Segments(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0) path = path[..q];
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
