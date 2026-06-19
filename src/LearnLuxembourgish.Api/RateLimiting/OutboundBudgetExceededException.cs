namespace LearnLuxembourgish.Api.RateLimiting;

/// <summary>
/// Thrown by <see cref="IGrammarService"/> when the outbound LLM call budget is exhausted.
/// Callers (e.g. <c>TranslationsController</c>) should catch this and return HTTP 429.
/// </summary>
public sealed class OutboundBudgetExceededException : Exception
{
    public TokenBucketResult Result { get; }

    public OutboundBudgetExceededException(TokenBucketResult result)
        : base("Outbound LLM call budget exhausted.")
    {
        Result = result;
    }
}
