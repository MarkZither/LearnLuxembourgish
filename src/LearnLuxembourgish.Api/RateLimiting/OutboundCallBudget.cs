using Microsoft.Extensions.Options;

namespace LearnLuxembourgish.Api.RateLimiting;

/// <summary>
/// Wraps <see cref="IRateLimitStore"/> with the fixed key <c>outbound:llm</c>
/// and reads bucket parameters from <see cref="RateLimitOptions.Outbound"/>.
/// </summary>
public sealed class OutboundCallBudget : IOutboundCallBudget
{
    private const string Key = "outbound:llm";

    private readonly IRateLimitStore _store;
    private readonly BucketOptions _options;

    public OutboundCallBudget(IRateLimitStore store, IOptions<RateLimitOptions> options)
    {
        _store = store;
        _options = options.Value.Outbound.Llm;
    }

    public Task<TokenBucketResult> TryConsumeAsync(CancellationToken ct = default)
        => _store.TryConsumeAsync(Key, _options.MaxTokens, _options.Window, ct);
}
