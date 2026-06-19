using System.Collections.Concurrent;

namespace LearnLuxembourgish.Api.RateLimiting;

public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private sealed class BucketEntry
    {
        public double TokensRemaining;
        public DateTimeOffset LastInteractionAt;
        // One lock per bucket — narrows contention to a single key.
        public readonly SemaphoreSlim Lock = new(1, 1);
    }

    private readonly ConcurrentDictionary<string, BucketEntry> _buckets = new();
    private readonly TimeProvider _clock;

    public InMemoryRateLimitStore(TimeProvider clock)
    {
        _clock = clock;
    }

    public async Task<TokenBucketResult> TryConsumeAsync(
        string key, int maxTokens, TimeSpan window, CancellationToken ct = default)
    {
        var entry = _buckets.GetOrAdd(key, _ => new BucketEntry
        {
            TokensRemaining = maxTokens,
            LastInteractionAt = _clock.GetUtcNow()
        });

        await entry.Lock.WaitAsync(ct);
        try
        {
            var now = _clock.GetUtcNow();
            var elapsed = now - entry.LastInteractionAt;

            // Continuous refill: accrue tokens proportional to elapsed time.
            var refillRate = (double)maxTokens / window.TotalSeconds;
            var accrued = elapsed.TotalSeconds * refillRate;
            entry.TokensRemaining = Math.Min(maxTokens, entry.TokensRemaining + accrued);
            entry.LastInteractionAt = now;

            if (entry.TokensRemaining >= 1.0)
            {
                entry.TokensRemaining -= 1.0;
                var remaining = (int)Math.Floor(entry.TokensRemaining);
                // ResetAt: time until the next full token accrues.
                var secondsToNextToken = entry.TokensRemaining < 1.0
                    ? (1.0 - entry.TokensRemaining) / refillRate
                    : 0.0;
                var resetAt = now.AddSeconds(secondsToNextToken);
                return new TokenBucketResult(true, remaining, resetAt);
            }
            else
            {
                // Time until at least one token is available.
                var secondsToNextToken = (1.0 - entry.TokensRemaining) / refillRate;
                var resetAt = now.AddSeconds(secondsToNextToken);
                return new TokenBucketResult(false, 0, resetAt);
            }
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(true);
}
