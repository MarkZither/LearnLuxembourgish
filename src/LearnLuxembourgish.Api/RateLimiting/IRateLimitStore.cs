namespace LearnLuxembourgish.Api.RateLimiting;

public interface IRateLimitStore
{
    Task<TokenBucketResult> TryConsumeAsync(
        string key, int maxTokens, TimeSpan window, CancellationToken ct = default);

    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}
