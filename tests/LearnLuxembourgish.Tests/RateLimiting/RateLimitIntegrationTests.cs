using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using LearnLuxembourgish.Api.RateLimiting;
using LearnLuxembourgish.Api.Services;
using LearnLuxembourgish.Data.Shared;
using LearnLuxembourgish.Shared.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace LearnLuxembourgish.Tests.RateLimiting;

/// <summary>
/// Integration tests covering all 11 spec acceptance scenarios from
/// docs/features/rate-limiting/spec.md, using WebApplicationFactory with
/// a FakeTimeProvider and mocked translation/grammar services.
///
/// Each test creates its own factory to get a fresh InMemoryRateLimitStore.
/// </summary>
public class RateLimitIntegrationTests
{
    // JWT config matching appsettings.json
    private const string JwtKey      = "YourVerySecureSecretKeyForLearnLuxembourgishApplicationThatIsAtLeast256BitsLong";
    private const string JwtIssuer   = "LearnLuxembourgish";
    private const string JwtAudience = "LearnLuxembourgish";

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    private sealed class RateLimitFactory : WebApplicationFactory<Program>
    {
        private readonly FakeTimeProvider _clock;
        private readonly Action<IServiceCollection>? _extraServices;

        public FakeTimeProvider Clock => _clock;

        public RateLimitFactory(FakeTimeProvider? clock = null, Action<IServiceCollection>? extraServices = null)
        {
            _clock = clock ?? new FakeTimeProvider();
            _extraServices = extraServices;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                // Replace TimeProvider singleton so InMemoryRateLimitStore uses the fake clock.
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);

                // Replace SQLite DbContext with in-memory database (unique to this factory).
                // Remove ALL DbContext-related descriptors that EF registers
                // to avoid the "two providers registered" exception.
                var efDescriptors = services
                    .Where(d => d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true
                                || d.ServiceType == typeof(LearnLuxembourgishDbContext)
                                || d.ImplementationType?.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true)
                    .ToList();
                foreach (var d in efDescriptors)
                    services.Remove(d);

                services.AddDbContext<LearnLuxembourgishDbContext>(
                    o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

                // Stub translation service — avoids real DeepL network calls.
                services.RemoveAll<ITranslationService>();
                var translationMock = new Mock<ITranslationService>();
                translationMock.Setup(s => s.TranslateAsync(
                        It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TranslationResult
                    {
                        OriginalText    = "Hello",
                        SourceLanguage  = "EN",
                        TranslatedText  = "Moien",
                        Provider        = "test"
                    });
                services.AddScoped<ITranslationService>(_ => translationMock.Object);

                // Stub grammar service — avoids real LLM calls; returns null (no grammar available).
                services.RemoveAll<IGrammarService>();
                var grammarMock = new Mock<IGrammarService>();
                grammarMock.Setup(s => s.ExplainGrammarAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                        It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string?)null);
                grammarMock.Setup(s => s.ConjugateVerbsAsync(
                        It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(),
                        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((List<VerbConjugationTable>?)null);
                services.AddScoped<IGrammarService>(_ => grammarMock.Object);

                // Apply scenario-specific overrides (e.g. US5 grammar mock behaviour).
                _extraServices?.Invoke(services);

                // Trust all networks for X-Forwarded-For in the test environment so
                // the TestServer's loopback connection is treated as a trusted proxy.
                services.Configure<ForwardedHeadersOptions>(opts =>
                {
                    opts.KnownIPNetworks.Clear();
                    opts.KnownProxies.Clear();
                });
            });
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HttpClient CreateClient(
        RateLimitFactory factory,
        string? ip = null,
        string? bearerToken = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        if (ip is not null)
            client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);

        if (bearerToken is not null)
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

        return client;
    }

    private static StringContent TranslationBody()
        => new("""{"text":"Hello","sourceLanguage":"EN"}""", Encoding.UTF8, "application/json");

    private static string MakeJwt(string userId, string role = "User")
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer:   JwtIssuer,
            audience: JwtAudience,
            claims:   [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)],
            expires:  DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string? GetHeader(HttpResponseMessage response, string name)
    {
        response.Headers.TryGetValues(name, out var vals);
        return vals?.FirstOrDefault();
    }

    // -------------------------------------------------------------------------
    // US1 — Anonymous per-IP limit (spec §3.1)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task US1_AC1_AnonymousFirstRequest_Returns200_WithRateLimitHeaders()
    {
        await using var factory = new RateLimitFactory();
        var client = CreateClient(factory, ip: "1.2.3.4");

        var response = await client.PostAsync("/api/translations", TranslationBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1",  GetHeader(response, "X-RateLimit-Limit"));
        Assert.Equal("0",  GetHeader(response, "X-RateLimit-Remaining"));
        Assert.NotNull(GetHeader(response, "X-RateLimit-Reset"));
    }

    [Fact]
    public async Task US1_AC2_AnonymousSecondRequest_WithinWindow_Returns429_WithRetryAfter()
    {
        await using var factory = new RateLimitFactory();
        var client = CreateClient(factory, ip: "1.2.3.4");

        // First request consumes the single anonymous token.
        await client.PostAsync("/api/translations", TranslationBody());

        var response = await client.PostAsync("/api/translations", TranslationBody());

        Assert.Equal(429, (int)response.StatusCode);
        Assert.NotNull(GetHeader(response, "Retry-After"));
        Assert.Equal("per-identity-limit-exceeded", GetHeader(response, "X-RateLimit-Error"));
    }

    // -------------------------------------------------------------------------
    // US2 — Global anonymous limit (spec §3.2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task US2_AC1_GlobalAnonCapExhausted_NewIpGets429_GlobalLimitError()
    {
        await using var factory = new RateLimitFactory();

        // 10 distinct IPs each consume one token — global cap reached.
        for (int i = 1; i <= 10; i++)
        {
            var c = CreateClient(factory, ip: $"10.0.0.{i}");
            await c.PostAsync("/api/translations", TranslationBody());
        }

        // 11th request from a brand-new IP must be rejected at the global level.
        var last = CreateClient(factory, ip: "10.0.0.100");
        var response = await last.PostAsync("/api/translations", TranslationBody());

        Assert.Equal(429, (int)response.StatusCode);
        Assert.Equal("global-limit-exceeded", GetHeader(response, "X-RateLimit-Error"));
    }

    [Fact]
    public async Task US2_AC2_AfterGlobalWindowResets_NewRequestAccepted()
    {
        await using var factory = new RateLimitFactory();

        // Exhaust the global anonymous cap.
        for (int i = 1; i <= 10; i++)
        {
            var c = CreateClient(factory, ip: $"10.1.0.{i}");
            await c.PostAsync("/api/translations", TranslationBody());
        }

        // Verify the cap is indeed hit.
        var blocked = CreateClient(factory, ip: "10.1.0.100");
        var r1 = await blocked.PostAsync("/api/translations", TranslationBody());
        Assert.Equal(429, (int)r1.StatusCode);

        // Advance clock past the 60-minute window — all tokens refill.
        factory.Clock.Advance(TimeSpan.FromMinutes(61));

        // New request from a fresh IP should now succeed.
        var fresh = CreateClient(factory, ip: "10.1.0.200");
        var r2 = await fresh.PostAsync("/api/translations", TranslationBody());
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    // -------------------------------------------------------------------------
    // US3 — Authenticated per-user quota (spec §3.3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task US3_AC1_AuthenticatedFifthRequest_Returns200_WithRemainingZero()
    {
        await using var factory = new RateLimitFactory();
        var client = CreateClient(factory, bearerToken: MakeJwt("alice"));

        // Requests 1–4.
        for (int i = 0; i < 4; i++)
            await client.PostAsync("/api/translations", TranslationBody());

        // 5th (last allowed) request.
        var response = await client.PostAsync("/api/translations", TranslationBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("5", GetHeader(response, "X-RateLimit-Limit"));
        Assert.Equal("0", GetHeader(response, "X-RateLimit-Remaining"));
    }

    [Fact]
    public async Task US3_AC2_AuthenticatedSixthRequest_Returns429()
    {
        await using var factory = new RateLimitFactory();
        var client = CreateClient(factory, bearerToken: MakeJwt("alice"));

        for (int i = 0; i < 5; i++)
            await client.PostAsync("/api/translations", TranslationBody());

        var response = await client.PostAsync("/api/translations", TranslationBody());

        Assert.Equal(429, (int)response.StatusCode);
        Assert.Equal("per-identity-limit-exceeded", GetHeader(response, "X-RateLimit-Error"));
        Assert.NotNull(GetHeader(response, "Retry-After"));
    }

    [Fact]
    public async Task US3_AC3_AuthGlobalCapExhausted_FreshUserGets429_GlobalLimitError()
    {
        await using var factory = new RateLimitFactory();

        // 10 users × 5 requests each = 50 — exhausts the global authenticated cap.
        for (int u = 0; u < 10; u++)
        {
            var c = CreateClient(factory, bearerToken: MakeJwt($"user-{u}"));
            for (int r = 0; r < 5; r++)
                await c.PostAsync("/api/translations", TranslationBody());
        }

        // A fresh user with no personal usage should be rejected by the global cap.
        var fresh = CreateClient(factory, bearerToken: MakeJwt("user-fresh"));
        var response = await fresh.PostAsync("/api/translations", TranslationBody());

        Assert.Equal(429, (int)response.StatusCode);
        Assert.Equal("global-limit-exceeded", GetHeader(response, "X-RateLimit-Error"));
    }

    // -------------------------------------------------------------------------
    // US4 — Owner/Admin unlimited access (spec §3.4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task US4_AC1_AdminAfterAllLimitsExhausted_Returns200_NoRateLimitHeaders()
    {
        await using var factory = new RateLimitFactory();

        // Exhaust anonymous global cap.
        for (int i = 1; i <= 10; i++)
        {
            var c = CreateClient(factory, ip: $"172.16.0.{i}");
            await c.PostAsync("/api/translations", TranslationBody());
        }

        // Exhaust authenticated global cap (50 requests).
        for (int u = 0; u < 10; u++)
        {
            var c = CreateClient(factory, bearerToken: MakeJwt($"exhaust-{u}"));
            for (int r = 0; r < 5; r++)
                await c.PostAsync("/api/translations", TranslationBody());
        }

        var admin    = CreateClient(factory, bearerToken: MakeJwt("admin-1", role: "Admin"));
        var response = await admin.PostAsync("/api/translations", TranslationBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Admin bypass path must not add rate-limit headers (plan.md §2 step 4).
        Assert.Null(GetHeader(response, "X-RateLimit-Limit"));
        Assert.Null(GetHeader(response, "X-RateLimit-Remaining"));
        Assert.Null(GetHeader(response, "X-RateLimit-Reset"));
    }

    [Fact]
    public async Task US4_AC2_AdminNeverGets429_AcrossMultipleRequests()
    {
        await using var factory = new RateLimitFactory();
        var admin = CreateClient(factory, bearerToken: MakeJwt("admin-1", role: "Admin"));

        // Far exceeds any configured per-user or global quota.
        for (int i = 0; i < 20; i++)
        {
            var response = await admin.PostAsync("/api/translations", TranslationBody());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // -------------------------------------------------------------------------
    // US5 — Outbound LLM budget (spec §3.5)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task US5_AC1_OutboundBudgetExhausted_Returns429_WithOutboundErrorCode()
    {
        // Override the grammar service to simulate exhausted outbound budget.
        // The real GrammarService throws OutboundBudgetExceededException when
        // IOutboundCallBudget.TryConsumeAsync returns Allowed=false.
        // The controller catches this and returns 429 outbound-budget-exceeded.
        var resetAt = DateTimeOffset.UtcNow.AddHours(1);

        await using var factory = new RateLimitFactory(extraServices: services =>
        {
            services.RemoveAll<IGrammarService>();
            var mock = new Mock<IGrammarService>();
            mock.Setup(s => s.ExplainGrammarAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OutboundBudgetExceededException(
                    new TokenBucketResult(false, 0, resetAt)));
            services.AddScoped<IGrammarService>(_ => mock.Object);
        });

        // Use admin to bypass inbound limits — the only active limit is the outbound budget.
        var admin    = CreateClient(factory, bearerToken: MakeJwt("admin-1", role: "Admin"));
        var response = await admin.PostAsync("/api/translations", TranslationBody());

        Assert.Equal(429, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("outbound-budget-exceeded", body);
    }

    [Fact]
    public async Task US5_AC2_AfterOutboundWindowResets_RequestSucceeds()
    {
        // Grammar service: first call simulates exhausted budget (throws),
        // second call simulates restored budget (returns null → controller returns 200).
        var resetAt = DateTimeOffset.UtcNow.AddHours(1);

        await using var factory = new RateLimitFactory(extraServices: services =>
        {
            services.RemoveAll<IGrammarService>();
            var mock = new Mock<IGrammarService>();
            mock.SetupSequence(s => s.ExplainGrammarAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OutboundBudgetExceededException(
                    new TokenBucketResult(false, 0, resetAt)))
                .ReturnsAsync((string?)null);
            mock.Setup(s => s.ConjugateVerbsAsync(
                    It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<VerbConjugationTable>?)null);
            services.AddScoped<IGrammarService>(_ => mock.Object);
        });

        var admin = CreateClient(factory, bearerToken: MakeJwt("admin-1", role: "Admin"));

        // First request — budget exhausted.
        var first = await admin.PostAsync("/api/translations", TranslationBody());
        Assert.Equal(429, (int)first.StatusCode);

        // Second request — budget restored after window reset.
        var second = await admin.PostAsync("/api/translations", TranslationBody());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}
