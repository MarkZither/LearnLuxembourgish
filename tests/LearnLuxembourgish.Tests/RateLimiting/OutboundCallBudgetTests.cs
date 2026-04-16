using LearnLuxembourgish.Api.RateLimiting;
using LearnLuxembourgish.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LearnLuxembourgish.Tests.RateLimiting;

// ---------------------------------------------------------------------------
// OutboundCallBudget unit tests
// ---------------------------------------------------------------------------

public class OutboundCallBudgetTests
{
    private static RateLimitOptions OptionsWithLlm(int maxTokens = 100, double windowMinutes = 60) =>
        new()
        {
            Outbound = new OutboundOptions
            {
                Llm = new BucketOptions { MaxTokens = maxTokens, WindowMinutes = windowMinutes }
            }
        };

    [Fact]
    public async Task TryConsumeAsync_WhenBudgetHasTokens_ReturnsAllowed()
    {
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.TryConsumeAsync(
                "outbound:llm", 100, TimeSpan.FromMinutes(60), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TokenBucketResult(true, 99, DateTimeOffset.UtcNow.AddHours(1)));

        var budget = new OutboundCallBudget(store.Object, Options.Create(OptionsWithLlm()));

        var result = await budget.TryConsumeAsync();

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task TryConsumeAsync_WhenBudgetExhausted_ReturnsDenied()
    {
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.TryConsumeAsync(
                "outbound:llm", 100, TimeSpan.FromMinutes(60), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TokenBucketResult(false, 0, DateTimeOffset.UtcNow.AddHours(1)));

        var budget = new OutboundCallBudget(store.Object, Options.Create(OptionsWithLlm()));

        var result = await budget.TryConsumeAsync();

        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task TryConsumeAsync_CallsStoreWithOutboundLlmKey()
    {
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.TryConsumeAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TokenBucketResult(true, 0, DateTimeOffset.UtcNow.AddHours(1)));

        var budget = new OutboundCallBudget(store.Object, Options.Create(OptionsWithLlm()));
        await budget.TryConsumeAsync();

        store.Verify(s => s.TryConsumeAsync(
            "outbound:llm",
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryConsumeAsync_UsesConfiguredMaxTokensAndWindow()
    {
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.TryConsumeAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TokenBucketResult(true, 0, DateTimeOffset.UtcNow.AddHours(1)));

        var budget = new OutboundCallBudget(store.Object, Options.Create(OptionsWithLlm(maxTokens: 50, windowMinutes: 30)));
        await budget.TryConsumeAsync();

        store.Verify(s => s.TryConsumeAsync(
            "outbound:llm",
            50,
            TimeSpan.FromMinutes(30),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

// ---------------------------------------------------------------------------
// GrammarService + IOutboundCallBudget integration
// ---------------------------------------------------------------------------

public class GrammarServiceOutboundBudgetTests
{
    private static IConfiguration EmptyConfig()
        => new ConfigurationBuilder().Build();

    [Fact]
    public async Task ExplainGrammarAsync_WhenBudgetExhausted_ThrowsOutboundBudgetExceededException()
    {
        var budget = new Mock<IOutboundCallBudget>();
        budget.Setup(b => b.TryConsumeAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenBucketResult(false, 0, DateTimeOffset.UtcNow.AddHours(1)));

        var service = new GrammarService(EmptyConfig(), NullLogger<GrammarService>.Instance, budget.Object);

        await Assert.ThrowsAsync<OutboundBudgetExceededException>(
            () => service.ExplainGrammarAsync("hello", "moien"));
    }

    [Fact]
    public async Task ConjugateVerbsAsync_WhenBudgetExhausted_ThrowsOutboundBudgetExceededException()
    {
        var budget = new Mock<IOutboundCallBudget>();
        budget.Setup(b => b.TryConsumeAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenBucketResult(false, 0, DateTimeOffset.UtcNow.AddHours(1)));

        var service = new GrammarService(EmptyConfig(), NullLogger<GrammarService>.Instance, budget.Object);

        await Assert.ThrowsAsync<OutboundBudgetExceededException>(
            () => service.ConjugateVerbsAsync(["goen", "sinn"]));
    }

    [Fact]
    public async Task ConjugateVerbsAsync_WithEmptyList_ReturnNullWithoutCallingBudget()
    {
        // Empty infinitives list short-circuits before budget check.
        var budget = new Mock<IOutboundCallBudget>();

        var service = new GrammarService(EmptyConfig(), NullLogger<GrammarService>.Instance, budget.Object);
        var result = await service.ConjugateVerbsAsync([]);

        Assert.Null(result);
        budget.Verify(b => b.TryConsumeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
