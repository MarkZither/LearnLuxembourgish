using LearnLuxembourgish.Api.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LearnLuxembourgish.Tests.RateLimiting;

public class InMemoryRateLimitStoreTests
{
    private static InMemoryRateLimitStore CreateStore(FakeTimeProvider clock)
        => new(clock);

    // -------------------------------------------------------------------------
    // Basic allow / deny
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FullBucket_AllowsFirstRequest()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        var result = await store.TryConsumeAsync("key", maxTokens: 3, TimeSpan.FromMinutes(60));

        Assert.True(result.Allowed);
        Assert.Equal(2, result.Remaining);
    }

    [Fact]
    public async Task ExhaustedBucket_RejectsNextRequest()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        // Drain the bucket.
        for (int i = 0; i < 3; i++)
            await store.TryConsumeAsync("key", maxTokens: 3, TimeSpan.FromMinutes(60));

        var result = await store.TryConsumeAsync("key", maxTokens: 3, TimeSpan.FromMinutes(60));

        Assert.False(result.Allowed);
        Assert.Equal(0, result.Remaining);
    }

    [Fact]
    public async Task SingleTokenBucket_AllowsThenDenies()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        var first = await store.TryConsumeAsync("ip:1.2.3.4", maxTokens: 1, TimeSpan.FromHours(1));
        var second = await store.TryConsumeAsync("ip:1.2.3.4", maxTokens: 1, TimeSpan.FromHours(1));

        Assert.True(first.Allowed);
        Assert.Equal(0, first.Remaining);
        Assert.False(second.Allowed);
    }

    // -------------------------------------------------------------------------
    // Refill
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TokensRefillProportionallyAfterElapsedTime()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        // Drain a 2-token, 60-minute bucket.
        await store.TryConsumeAsync("key", maxTokens: 2, TimeSpan.FromMinutes(60));
        await store.TryConsumeAsync("key", maxTokens: 2, TimeSpan.FromMinutes(60));

        // Advance by 30 minutes — should refill exactly 1 token.
        clock.Advance(TimeSpan.FromMinutes(30));

        var result = await store.TryConsumeAsync("key", maxTokens: 2, TimeSpan.FromMinutes(60));

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task FullWindowElapsed_RefillsToMaxTokens()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        // Drain completely.
        await store.TryConsumeAsync("key", maxTokens: 1, TimeSpan.FromHours(1));
        var denied = await store.TryConsumeAsync("key", maxTokens: 1, TimeSpan.FromHours(1));
        Assert.False(denied.Allowed);

        // Advance a full window.
        clock.Advance(TimeSpan.FromHours(1));

        var result = await store.TryConsumeAsync("key", maxTokens: 1, TimeSpan.FromHours(1));
        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task PartialElapsed_DoesNotRefillEnoughForRequest()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        // Drain a 1-token, 60-minute bucket.
        await store.TryConsumeAsync("key", maxTokens: 1, TimeSpan.FromMinutes(60));

        // Advance only 10 minutes — not enough for 1 full token.
        clock.Advance(TimeSpan.FromMinutes(10));

        var result = await store.TryConsumeAsync("key", maxTokens: 1, TimeSpan.FromMinutes(60));
        Assert.False(result.Allowed);
    }

    // -------------------------------------------------------------------------
    // ResetAt
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResetAt_IsInTheFutureWhenRequestDenied()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        await store.TryConsumeAsync("key", maxTokens: 1, TimeSpan.FromHours(1));
        var result = await store.TryConsumeAsync("key", maxTokens: 1, TimeSpan.FromHours(1));

        Assert.False(result.Allowed);
        Assert.True(result.ResetAt > clock.GetUtcNow());
    }

    // -------------------------------------------------------------------------
    // Key isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DifferentKeys_HaveIndependentBuckets()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        // Drain key A.
        await store.TryConsumeAsync("keyA", maxTokens: 1, TimeSpan.FromHours(1));

        var resultB = await store.TryConsumeAsync("keyB", maxTokens: 1, TimeSpan.FromHours(1));

        Assert.True(resultB.Allowed);
    }

    // -------------------------------------------------------------------------
    // Concurrency
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentCalls_DoNotExceedQuota()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        const int quota = 5;
        const int threads = 20;

        var tasks = Enumerable.Range(0, threads)
            .Select(_ => store.TryConsumeAsync("key", maxTokens: quota, TimeSpan.FromHours(1)));

        var results = await Task.WhenAll(tasks);

        var allowed = results.Count(r => r.Allowed);
        Assert.Equal(quota, allowed);
    }

    // -------------------------------------------------------------------------
    // IsAvailableAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsAvailableAsync_AlwaysReturnsTrue()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock);

        Assert.True(await store.IsAvailableAsync());
    }
}
