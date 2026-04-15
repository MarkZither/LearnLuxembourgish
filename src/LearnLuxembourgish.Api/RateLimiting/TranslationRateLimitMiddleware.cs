using System.Net;
using System.Security.Claims;
using LearnLuxembourgish.Api.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LearnLuxembourgish.Api.RateLimiting;

/// <summary>
/// Enforces all inbound rate limits on POST /api/translations.
/// Must be registered after UseAuthentication() + UseAuthorization()
/// so that HttpContext.User is populated before this middleware runs.
/// SEC-002: Constructor asserts IAuthenticationSchemeProvider is in DI;
///          fails fast at startup if middleware is registered before UseAuthentication().
/// </summary>
public sealed class TranslationRateLimitMiddleware
{
    private const string TranslationPath = "/api/translations";
    private const string AdminRole = "Admin";

    private readonly RequestDelegate _next;
    private readonly IRateLimitStore _store;
    private readonly RateLimitOptions _options;
    private readonly ILogger<TranslationRateLimitMiddleware> _logger;
    private readonly TimeProvider _clock;

    // SEC-002: IAuthenticationSchemeProvider is only resolvable after
    // AddAuthentication() has been called.  Requesting it here forces a
    // startup-time DI resolution failure if UseAuthentication() was skipped
    // or the middleware was placed before authentication in the pipeline.
    public TranslationRateLimitMiddleware(
        RequestDelegate next,
        IRateLimitStore store,
        IOptions<RateLimitOptions> options,
        ILogger<TranslationRateLimitMiddleware> logger,
        TimeProvider clock,
        IAuthenticationSchemeProvider _, // SEC-002 guard — see class doc
        IServiceProvider services)
    {
        // Verify at startup that authentication middleware is configured.
        var schemeProvider = services.GetService<IAuthenticationSchemeProvider>()
            ?? throw new InvalidOperationException(
                $"{nameof(TranslationRateLimitMiddleware)} requires authentication middleware. " +
                "Ensure AddAuthentication() is called and app.UseAuthentication() is placed " +
                "before app.UseMiddleware<TranslationRateLimitMiddleware>() in Program.cs.");

        _next = next;
        _store = store;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only applies to POST /api/translations (FR-009).
        if (!IsTranslationPost(context))
        {
            await _next(context);
            return;
        }

        // SEC-005: wrap all store calls — any exception is treated as store-unavailable.
        try
        {
            await EnforceAsync(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Rate limit store threw an unexpected exception; applying fail-closed policy");
            await WriteFail(context, "store-unavailable", "Rate limit service error. Please try again later.");
        }
    }

    private async Task EnforceAsync(HttpContext context)
    {
        // Step 1 – store availability check (ADR 0002 fail-closed).
        bool available;
        try
        {
            available = await _store.IsAvailableAsync(context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IRateLimitStore.IsAvailableAsync threw; applying fail-closed policy");
            await WriteFail(context, "store-unavailable", "Rate limit service unavailable. Please try again later.");
            return;
        }

        if (!available)
        {
            _logger.LogWarning("Rate limit store unavailable — request rejected (fail-closed)");
            await WriteFail(context, "store-unavailable", "Rate limit service unavailable. Please try again later.");
            return;
        }

        // Step 2 – Owner/Admin bypass (FR-005, US4).
        if (context.User.IsInRole(AdminRole))
        {
            await _next(context);
            return;
        }

        // Step 3 – determine tier and identity.
        bool isAuthenticated = context.User.Identity?.IsAuthenticated == true;

        if (isAuthenticated)
        {
            await EnforceAuthenticatedAsync(context);
        }
        else
        {
            await EnforceAnonymousAsync(context);
        }
    }

    private async Task EnforceAnonymousAsync(HttpContext context)
    {
        var policy = _options.Unauthenticated;

        // Step 4 – global anonymous limit first (FR-012).
        var globalResult = await _store.TryConsumeAsync(
            "global:anon",
            policy.Global.MaxTokens,
            policy.Global.Window,
            context.RequestAborted);

        if (!globalResult.Allowed)
        {
            _logger.LogInformation("Global anonymous rate limit exceeded");
            await WriteRateLimited(context, "global-limit-exceeded", globalResult,
                "Global anonymous request capacity is exhausted. Create an account for a higher quota.");
            return;
        }

        // Step 5 – per-IP limit.
        var ip = GetClientIp(context);
        var perIpResult = await _store.TryConsumeAsync(
            $"anon:{ip}",
            policy.PerIp.MaxTokens,
            policy.PerIp.Window,
            context.RequestAborted);

        if (!perIpResult.Allowed)
        {
            _logger.LogInformation("Per-IP anonymous rate limit exceeded for {Ip}", ip);
            await WriteRateLimited(context, "per-identity-limit-exceeded", perIpResult,
                "You have reached the anonymous request limit. Create an account for a higher quota.");
            return;
        }

        AddRateLimitHeaders(context, policy.PerIp.MaxTokens, perIpResult);
        await _next(context);
    }

    private async Task EnforceAuthenticatedAsync(HttpContext context)
    {
        var policy = _options.Authenticated;

        // Step 4 – global authenticated limit first (FR-012).
        var globalResult = await _store.TryConsumeAsync(
            "global:auth",
            policy.Global.MaxTokens,
            policy.Global.Window,
            context.RequestAborted);

        if (!globalResult.Allowed)
        {
            _logger.LogInformation("Global authenticated rate limit exceeded");
            await WriteRateLimited(context, "global-limit-exceeded", globalResult,
                "Global authenticated request capacity is exhausted. Please try again later.");
            return;
        }

        // Step 5 – per-user limit.
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue("sub")
                     ?? context.User.Identity!.Name
                     ?? "unknown";

        var perUserResult = await _store.TryConsumeAsync(
            $"user:{userId}",
            policy.PerUser.MaxTokens,
            policy.PerUser.Window,
            context.RequestAborted);

        if (!perUserResult.Allowed)
        {
            _logger.LogInformation("Per-user rate limit exceeded for {UserId}", userId);
            await WriteRateLimited(context, "per-identity-limit-exceeded", perUserResult,
                "You have reached your hourly translation quota. Please try again later.");
            return;
        }

        AddRateLimitHeaders(context, policy.PerUser.MaxTokens, perUserResult);
        await _next(context);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsTranslationPost(HttpContext context)
        => context.Request.Method == HttpMethods.Post
           && context.Request.Path.Equals(TranslationPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the client IP after UseForwardedHeaders() has rewritten
    /// RemoteIpAddress.  Do NOT read X-Forwarded-For here directly (SEC-004).
    /// </summary>
    private static string GetClientIp(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static void AddRateLimitHeaders(
        HttpContext context, int limit, TokenBucketResult result)
    {
        context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] =
            result.ResetAt.ToUnixTimeSeconds().ToString();
    }

    private Task WriteRateLimited(
        HttpContext context, string errorCode, TokenBucketResult result, string message)
    {
        var now = _clock.GetUtcNow();
        var retryAfter = Math.Max(1, (int)Math.Ceiling((result.ResetAt - now).TotalSeconds));
        return RateLimitResponseWriter.WriteTooManyRequestsAsync(
            context, errorCode, retryAfter, message, context.RequestAborted);
    }

    private static Task WriteFail(HttpContext context, string errorCode, string message)
        => RateLimitResponseWriter.WriteTooManyRequestsAsync(
            context, errorCode, retryAfterSeconds: 60, message, context.RequestAborted);
}
