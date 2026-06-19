using System.Net;
using System.Security.Claims;
using LearnLuxembourgish.Api.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace LearnLuxembourgish.Tests.RateLimiting;

/// <summary>
/// Unit tests for TranslationRateLimitMiddleware using a mock IRateLimitStore.
/// These tests verify middleware routing, ordering, header presence, and error codes
/// without touching the in-memory store implementation.
/// </summary>
public class TranslationRateLimitMiddlewareTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly RateLimitOptions DefaultOptions = new()
    {
        Unauthenticated = new TierOptions
        {
            PerIp   = new BucketOptions { MaxTokens = 1,  WindowMinutes = 60 },
            Global  = new BucketOptions { MaxTokens = 10, WindowMinutes = 60 }
        },
        Authenticated = new TierOptions
        {
            PerUser = new BucketOptions { MaxTokens = 5,  WindowMinutes = 60 },
            Global  = new BucketOptions { MaxTokens = 50, WindowMinutes = 60 }
        }
    };

    private static TranslationRateLimitMiddleware BuildMiddleware(
        RequestDelegate next,
        IRateLimitStore store,
        RateLimitOptions? options = null,
        TimeProvider? clock = null)
    {
        var schemeProvider = new Mock<IAuthenticationSchemeProvider>();
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IAuthenticationSchemeProvider)))
                .Returns(schemeProvider.Object);

        return new TranslationRateLimitMiddleware(
            next,
            store,
            Options.Create(options ?? DefaultOptions),
            NullLogger<TranslationRateLimitMiddleware>.Instance,
            clock ?? TimeProvider.System,
            schemeProvider.Object,
            services.Object);
    }

    private static Mock<IRateLimitStore> AllowingStore()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
        store.Setup(s => s.TryConsumeAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TokenBucketResult(true, 0, now.AddHours(1)));
        return store;
    }

    private static Mock<IRateLimitStore> DenyingStore(string? onKey = null)
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        if (onKey is null)
        {
            // Deny all TryConsume calls.
            store.Setup(s => s.TryConsumeAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new TokenBucketResult(false, 0, now.AddHours(1)));
        }
        else
        {
            // Allow everything except the specified key.
            store.Setup(s => s.TryConsumeAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new TokenBucketResult(true, 0, now.AddHours(1)));
            store.Setup(s => s.TryConsumeAsync(
                    onKey, It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new TokenBucketResult(false, 0, now.AddHours(1)));
        }
        return store;
    }

    private static HttpContext BuildContext(
        string method = "POST",
        string path = "/api/translations",
        ClaimsPrincipal? user = null,
        IPAddress? remoteIp = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (user is not null) context.User = user;
        if (remoteIp is not null) context.Connection.RemoteIpAddress = remoteIp;
        context.Response.Body = new System.IO.MemoryStream();
        return context;
    }

    private static ClaimsPrincipal AuthUser(string userId = "user-1", string role = "User")
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        }, authenticationType: "Test"));

    private static ClaimsPrincipal AdminUser()
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin-1"),
            new Claim(ClaimTypes.Role, "Admin")
        }, authenticationType: "Test"));

    // -------------------------------------------------------------------------
    // Path filtering (FR-009)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NonTranslationPath_PassesThrough_WithoutCallingStore()
    {
        var store = new Mock<IRateLimitStore>();
        bool nextCalled = false;
        var mw = BuildMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, store.Object);

        var ctx = BuildContext(path: "/api/flashcards");
        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
        store.Verify(s => s.TryConsumeAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTranslationPath_PassesThrough_WithoutCallingStore()
    {
        var store = new Mock<IRateLimitStore>();
        bool nextCalled = false;
        var mw = BuildMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, store.Object);

        var ctx = BuildContext(method: "GET", path: "/api/translations");
        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
        store.Verify(s => s.TryConsumeAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Store unavailable — fail-closed (ADR 0002 + SEC-005)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreUnavailable_Returns429_WithStoreUnavailableHeader()
    {
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var mw = BuildMiddleware(_ => Task.CompletedTask, store.Object);

        var ctx = BuildContext();
        await mw.InvokeAsync(ctx);

        Assert.Equal(429, ctx.Response.StatusCode);
        Assert.Equal("store-unavailable", ctx.Response.Headers["X-RateLimit-Error"].ToString());
    }

    [Fact]
    public async Task StoreIsAvailableThrows_Returns429_NotHttp500()
    {
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("store exploded"));
        var mw = BuildMiddleware(_ => Task.CompletedTask, store.Object);

        var ctx = BuildContext();
        await mw.InvokeAsync(ctx);

        Assert.Equal(429, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task TryConsumeThrows_Returns429_NotHttp500()
    {
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.TryConsumeAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new Exception("unexpected"));
        var mw = BuildMiddleware(_ => Task.CompletedTask, store.Object);

        var ctx = BuildContext();
        await mw.InvokeAsync(ctx);

        Assert.Equal(429, ctx.Response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Anonymous — per-IP (US1)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnonymousFirstRequest_Returns200_WithRateLimitHeaders()
    {
        bool nextCalled = false;
        var mw = BuildMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, AllowingStore().Object);
        var ctx = BuildContext(remoteIp: IPAddress.Parse("1.2.3.4"));

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.True(ctx.Response.Headers.ContainsKey("X-RateLimit-Limit"));
        Assert.True(ctx.Response.Headers.ContainsKey("X-RateLimit-Remaining"));
        Assert.True(ctx.Response.Headers.ContainsKey("X-RateLimit-Reset"));
    }

    [Fact]
    public async Task AnonymousPerIpLimitExceeded_Returns429_WithRetryAfter()
    {
        var mw = BuildMiddleware(_ => Task.CompletedTask, DenyingStore("anon:1.2.3.4").Object);
        var ctx = BuildContext(remoteIp: IPAddress.Parse("1.2.3.4"));

        await mw.InvokeAsync(ctx);

        Assert.Equal(429, ctx.Response.StatusCode);
        Assert.True(ctx.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal("per-identity-limit-exceeded", ctx.Response.Headers["X-RateLimit-Error"].ToString());
    }

    // -------------------------------------------------------------------------
    // Anonymous — global (US2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnonymousGlobalLimitExceeded_Returns429_BeforeCheckingPerIp()
    {
        var store = DenyingStore("global:anon");
        var mw = BuildMiddleware(_ => Task.CompletedTask, store.Object);
        var ctx = BuildContext(remoteIp: IPAddress.Parse("9.9.9.9"));

        await mw.InvokeAsync(ctx);

        Assert.Equal(429, ctx.Response.StatusCode);
        Assert.Equal("global-limit-exceeded", ctx.Response.Headers["X-RateLimit-Error"].ToString());

        // Per-IP counter must NOT have been touched (FR-012).
        store.Verify(s => s.TryConsumeAsync(
            It.Is<string>(k => k.StartsWith("anon:")),
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Authenticated — per-user (US3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AuthenticatedWithinQuota_Returns200_WithHeaders()
    {
        bool nextCalled = false;
        var mw = BuildMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, AllowingStore().Object);
        var ctx = BuildContext(user: AuthUser("alice"));

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.True(ctx.Response.Headers.ContainsKey("X-RateLimit-Limit"));
    }

    [Fact]
    public async Task AuthenticatedPerUserLimitExceeded_Returns429()
    {
        var mw = BuildMiddleware(_ => Task.CompletedTask, DenyingStore("user:alice").Object);
        var ctx = BuildContext(user: AuthUser("alice"));

        await mw.InvokeAsync(ctx);

        Assert.Equal(429, ctx.Response.StatusCode);
        Assert.Equal("per-identity-limit-exceeded", ctx.Response.Headers["X-RateLimit-Error"].ToString());
    }

    [Fact]
    public async Task AuthenticatedGlobalLimitExceeded_Returns429_DoesNotTouchPerUser()
    {
        var store = DenyingStore("global:auth");
        var mw = BuildMiddleware(_ => Task.CompletedTask, store.Object);
        var ctx = BuildContext(user: AuthUser("bob"));

        await mw.InvokeAsync(ctx);

        Assert.Equal(429, ctx.Response.StatusCode);
        Assert.Equal("global-limit-exceeded", ctx.Response.Headers["X-RateLimit-Error"].ToString());

        // per-user counter must NOT have been consumed (FR-012).
        store.Verify(s => s.TryConsumeAsync(
            It.Is<string>(k => k.StartsWith("user:")),
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Owner/Admin bypass (US4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Admin_AlwaysPasses_NoBucketChecks()
    {
        // Store is available; admin bypasses bucket checks but store.IsAvailableAsync is still called.
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        bool nextCalled = false;
        var mw = BuildMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, store.Object);
        var ctx = BuildContext(user: AdminUser());

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
        // Bucket TryConsume must NOT have been touched — admin bypasses all quotas.
        store.Verify(s => s.TryConsumeAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Admin_WhenStoreUnavailable_IsRejectedFailClosed()
    {
        // Per ADR 0002 and security-review-architecture.md: fail-closed is absolute.
        // Store availability is checked before the admin bypass (plan.md step 2 before step 4).
        // Admin receives 429 store-unavailable the same as any other caller.
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var mw = BuildMiddleware(_ => Task.CompletedTask, store.Object);
        var ctx = BuildContext(user: AdminUser());

        await mw.InvokeAsync(ctx);

        Assert.Equal(429, ctx.Response.StatusCode);
        Assert.Equal("store-unavailable", ctx.Response.Headers["X-RateLimit-Error"].ToString());
    }

    [Fact]
    public async Task Admin_Response_HasNoRateLimitHeaders()
    {
        var store = new Mock<IRateLimitStore>();
        var mw = BuildMiddleware(_ => Task.CompletedTask, store.Object);
        var ctx = BuildContext(user: AdminUser());

        await mw.InvokeAsync(ctx);

        Assert.False(ctx.Response.Headers.ContainsKey("X-RateLimit-Limit"));
        Assert.False(ctx.Response.Headers.ContainsKey("X-RateLimit-Remaining"));
        Assert.False(ctx.Response.Headers.ContainsKey("X-RateLimit-Reset"));
    }

    // -------------------------------------------------------------------------
    // 429 response shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RateLimited_Response_IncludesRetryAfterHeader()
    {
        var mw = BuildMiddleware(_ => Task.CompletedTask, DenyingStore().Object);
        var ctx = BuildContext(remoteIp: IPAddress.Parse("1.2.3.4"));

        await mw.InvokeAsync(ctx);

        Assert.Equal(429, ctx.Response.StatusCode);
        Assert.True(ctx.Response.Headers.ContainsKey("Retry-After"));
        Assert.True(int.Parse(ctx.Response.Headers["Retry-After"].ToString()) >= 1);
    }

    // -------------------------------------------------------------------------
    // Phase 4: authenticated quota edge cases and explicit admin bypass (US3/US4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Authenticated_LastAllowedRequest_Returns200_WithRemainingZero()
    {
        // Remaining=0 means this was the last token — but Allowed=true, so it's a 200.
        var now = DateTimeOffset.UtcNow;
        var store = new Mock<IRateLimitStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.TryConsumeAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TokenBucketResult(true, 0, now.AddHours(1)));

        bool nextCalled = false;
        var mw = BuildMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, store.Object);
        var ctx = BuildContext(user: AuthUser("alice"));

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.Equal(200, ctx.Response.StatusCode);
        // X-RateLimit-Remaining must reflect the store result even when it is 0.
        Assert.Equal("0", ctx.Response.Headers["X-RateLimit-Remaining"].ToString());
        // X-RateLimit-Limit must reflect the configured per-user MaxTokens.
        Assert.Equal(DefaultOptions.Authenticated.PerUser.MaxTokens.ToString(),
            ctx.Response.Headers["X-RateLimit-Limit"].ToString());
    }

    [Fact]
    public async Task Admin_WhenAuthQuotaExhausted_StillPasses200()
    {
        // Admin bypass (step 4) occurs after store availability check (step 2) but before
        // any TryConsume call.  Even if all buckets would deny, admin is never rate-limited.
        bool nextCalled = false;
        var mw = BuildMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, DenyingStore().Object);
        var ctx = BuildContext(user: AdminUser());

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.Equal(200, ctx.Response.StatusCode);
    }
}
