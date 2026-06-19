using System.Text.Json;

namespace LearnLuxembourgish.Api.RateLimiting;

/// <summary>
/// Writes RFC-7807-style 429 responses with all required rate limit headers.
/// Used by both the inbound middleware and the outbound budget controller catch block.
/// </summary>
internal static class RateLimitResponseWriter
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static async Task WriteTooManyRequestsAsync(
        HttpContext context,
        string errorCode,
        int retryAfterSeconds,
        string message,
        CancellationToken ct = default)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        context.Response.Headers["X-RateLimit-Error"] = errorCode;

        var body = new
        {
            status = 429,
            error = errorCode,
            message
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(body, _json), ct);
    }
}
