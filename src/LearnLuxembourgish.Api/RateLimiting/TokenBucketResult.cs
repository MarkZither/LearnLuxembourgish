namespace LearnLuxembourgish.Api.RateLimiting;

public record TokenBucketResult(bool Allowed, int Remaining, DateTimeOffset ResetAt);
