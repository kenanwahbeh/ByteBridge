using System;
using System.Collections.Generic;
using System.Linq;

namespace ByteBridge.Gateway;

/*
 * Slows down guessing of the API key.
 *
 * The key is 256 random bits, so guessing it is not realistic, but
 * nothing stopped a caller from sending wrong ones as fast as the tunnel
 * carries them, and every attempt costs the machine a request. After too
 * many refusals inside a window a caller is turned away for a while
 * before its key is even looked at.
 *
 * Callers are told apart by address. That is only as good as the address
 * the gateway is given, which is why the server passes Cloudflare's own
 * header (Cloudflare overwrites it, so a caller cannot choose it) and
 * never X-Forwarded-For (whose first entry the caller writes itself).
 *
 * In memory and bounded: a flood of distinct addresses cannot make the
 * table grow without limit, and a restart forgives everyone, which is
 * acceptable for a brake rather than an audit record.
 */
internal sealed class AuthFailureLimiter
{
    private readonly int _maxFailures;

    private readonly TimeSpan _window;

    private readonly TimeSpan _blockFor;

    private readonly int _maxTracked;

    private readonly Func<DateTime> _now;

    private readonly object _sync = new();

    private readonly Dictionary<string, Entry> _entries =
        new(StringComparer.Ordinal);

    private struct Entry
    {
        public int Failures;

        public DateTime WindowStart;

        public DateTime BlockedUntil;
    }

    public AuthFailureLimiter(
        int maxFailures = 10,
        TimeSpan? window = null,
        TimeSpan? blockFor = null,
        int maxTracked = 4096,
        Func<DateTime>? now = null)
    {
        _maxFailures = maxFailures;
        _window = window ?? TimeSpan.FromMinutes(1);
        _blockFor = blockFor ?? TimeSpan.FromMinutes(1);
        _maxTracked = maxTracked;
        _now = now ?? (() => DateTime.UtcNow);
    }

    /*
     * How many callers are being tracked. Only the tests look.
     */
    internal int TrackedCount
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    /*
     * True while the caller is being turned away. retryAfter is how much
     * longer, rounded up to a whole second for the Retry-After header.
     */
    public bool IsBlocked(string key, out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                var remaining = entry.BlockedUntil - _now();

                if (remaining > TimeSpan.Zero)
                {
                    retryAfter = TimeSpan.FromSeconds(
                        Math.Ceiling(remaining.TotalSeconds));

                    return true;
                }
            }
        }

        retryAfter = TimeSpan.Zero;

        return false;
    }

    public void RecordFailure(string key)
    {
        lock (_sync)
        {
            var now = _now();

            if (!_entries.TryGetValue(key, out var entry)
                || now - entry.WindowStart > _window)
            {
                MakeRoom(now);

                entry = new Entry
                {
                    WindowStart = now
                };
            }

            entry.Failures++;

            if (entry.Failures >= _maxFailures)
            {
                entry.BlockedUntil = now + _blockFor;

                // The next window starts clean once the block ends.
                entry.Failures = 0;
                entry.WindowStart = entry.BlockedUntil;
            }

            _entries[key] = entry;
        }
    }

    /*
     * A correct key wipes the slate, so a person who mistyped it a few
     * times is not left one slip from a block.
     */
    public void RecordSuccess(string key)
    {
        lock (_sync)
        {
            _entries.Remove(key);
        }
    }

    /*
     * Called with the lock held before a new address is added. Drops
     * whatever has expired first; if the table is still full of live
     * entries, the oldest goes, so it never grows past the bound.
     */
    private void MakeRoom(DateTime now)
    {
        if (_entries.Count < _maxTracked)
        {
            return;
        }

        var expired = _entries
            .Where(pair =>
                pair.Value.BlockedUntil <= now
                && now - pair.Value.WindowStart > _window)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in expired)
        {
            _entries.Remove(key);
        }

        if (_entries.Count < _maxTracked)
        {
            return;
        }

        var oldest = _entries
            .OrderBy(pair => pair.Value.WindowStart)
            .First()
            .Key;

        _entries.Remove(oldest);
    }
}
