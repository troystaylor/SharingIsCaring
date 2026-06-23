#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace ArdDiscovery.Services;

/// <summary>
/// Simple in-memory sliding window rate limiter.
/// Tracks request counts per key (user/IP) with a configurable window and limit.
/// For production, use Azure API Management or Front Door rate limiting.
/// </summary>
public class RateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    public RateLimiter(int maxRequests = 60, TimeSpan? window = null)
    {
        _maxRequests = maxRequests;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Check if the request should be allowed. Returns true if within limit.
    /// </summary>
    public bool TryAcquire(string key)
    {
        var sw = _windows.GetOrAdd(key, _ => new SlidingWindow(_maxRequests, _window));
        return sw.TryAcquire();
    }

    /// <summary>
    /// Get seconds until the window resets for retry-after headers.
    /// </summary>
    public int GetRetryAfterSeconds(string key)
    {
        if (_windows.TryGetValue(key, out var sw))
            return sw.SecondsUntilReset();
        return (int)_window.TotalSeconds;
    }

    private class SlidingWindow
    {
        private readonly int _max;
        private readonly TimeSpan _window;
        private readonly object _lock = new();
        private int _count;
        private DateTimeOffset _windowStart;

        public SlidingWindow(int max, TimeSpan window)
        {
            _max = max;
            _window = window;
            _windowStart = DateTimeOffset.UtcNow;
        }

        public bool TryAcquire()
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                if (now - _windowStart >= _window)
                {
                    // Reset window
                    _windowStart = now;
                    _count = 0;
                }

                if (_count >= _max)
                    return false;

                _count++;
                return true;
            }
        }

        public int SecondsUntilReset()
        {
            lock (_lock)
            {
                var elapsed = DateTimeOffset.UtcNow - _windowStart;
                var remaining = _window - elapsed;
                return Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            }
        }
    }
}
