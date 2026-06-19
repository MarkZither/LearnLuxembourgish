namespace LearnLuxembourgish.Api.RateLimiting;

public sealed class RateLimitOptions
{
    public TierOptions Unauthenticated { get; init; } = new();
    public TierOptions Authenticated { get; init; } = new();
    public OutboundOptions Outbound { get; init; } = new();
}

public sealed class TierOptions
{
    public BucketOptions PerIp { get; init; } = new() { MaxTokens = 1, WindowMinutes = 60 };
    public BucketOptions PerUser { get; init; } = new() { MaxTokens = 5, WindowMinutes = 60 };
    public BucketOptions Global { get; init; } = new() { MaxTokens = 10, WindowMinutes = 60 };
}

public sealed class OutboundOptions
{
    public BucketOptions Llm { get; init; } = new() { MaxTokens = 100, WindowMinutes = 60 };
}

public sealed class BucketOptions
{
    public int MaxTokens { get; init; }
    public double WindowMinutes { get; init; }

    public TimeSpan Window => TimeSpan.FromMinutes(WindowMinutes);
}
