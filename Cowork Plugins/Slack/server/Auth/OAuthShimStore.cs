using System.Collections.Concurrent;

namespace SlackCoworkMcp.Auth;

/// <summary>In-flight state stored between /authorize and /callback.</summary>
public sealed record CoworkAuthRequest(
    string CoworkRedirectUri,
    string CoworkState,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    DateTimeOffset ExpiresAt);

/// <summary>Token bundle stored under a shim-issued code; consumed once at /token.</summary>
public sealed record IssuedToken(
    string AccessToken,
    string? Scope,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string CoworkRedirectUri,
    DateTimeOffset ExpiresAt);

public interface IOAuthShimStore
{
    void StoreAuth(string state, CoworkAuthRequest req);
    CoworkAuthRequest? TakeAuth(string state);
    void StoreCode(string code, IssuedToken token);
    IssuedToken? TakeCode(string code);
}

/// <summary>
/// In-memory state store with lazy TTL pruning. Acceptable for this workload because
/// OAuth codes are consumed within seconds and the shim runs as a single replica or
/// idempotent replicas (the worst case on cold-replica routing is a single retry).
/// </summary>
public sealed class InMemoryOAuthShimStore : IOAuthShimStore
{
    private readonly ConcurrentDictionary<string, CoworkAuthRequest> _auth = new();
    private readonly ConcurrentDictionary<string, IssuedToken> _codes = new();

    public void StoreAuth(string state, CoworkAuthRequest req)
    {
        Prune();
        _auth[state] = req;
    }

    public CoworkAuthRequest? TakeAuth(string state)
    {
        if (!_auth.TryRemove(state, out var v)) return null;
        return v.ExpiresAt > DateTimeOffset.UtcNow ? v : null;
    }

    public void StoreCode(string code, IssuedToken token)
    {
        Prune();
        _codes[code] = token;
    }

    public IssuedToken? TakeCode(string code)
    {
        if (!_codes.TryRemove(code, out var v)) return null;
        return v.ExpiresAt > DateTimeOffset.UtcNow ? v : null;
    }

    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _auth)
        {
            if (kvp.Value.ExpiresAt <= now) _auth.TryRemove(kvp.Key, out _);
        }
        foreach (var kvp in _codes)
        {
            if (kvp.Value.ExpiresAt <= now) _codes.TryRemove(kvp.Key, out _);
        }
    }
}
