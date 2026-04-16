namespace LearnLuxembourgish.Api.RateLimiting;

/// <summary>
/// Guards outbound LLM calls against cost overruns.
/// Implemented by <see cref="OutboundCallBudget"/> which delegates to <see cref="IRateLimitStore"/>
/// with the fixed key <c>outbound:llm</c>.
/// Unlike inbound limits, this budget applies to ALL callers including Owner/Admin,
/// because it controls platform cost rather than per-user fairness.
/// </summary>
public interface IOutboundCallBudget
{
    /// <summary>
    /// Attempts to consume one token from the outbound LLM budget.
    /// Returns <see cref="TokenBucketResult.Allowed"/> = false when the budget is exhausted.
    /// </summary>
    Task<TokenBucketResult> TryConsumeAsync(CancellationToken ct = default);
}
