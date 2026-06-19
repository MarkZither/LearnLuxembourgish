using LearnLuxembourgish.Shared.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LearnLuxembourgish.Shared.Http;

/// <summary>
/// Scoped API client that shares the Blazor circuit scope with TokenProvider.
/// Avoids the IHttpClientFactory handler scope issue where DelegatingHandler
/// gets a different TokenProvider instance than the Blazor components.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly TokenProvider _tokenProvider;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(HttpClient httpClient, TokenProvider tokenProvider, ILogger<ApiClient> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken = default)
        => await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);

    public async Task<HttpResponseMessage> PostAsJsonAsync<T>(string url, T value, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(value)
        };
        return await SendAsync(request, cancellationToken);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string url, CancellationToken cancellationToken = default)
        => await SendAsync(new HttpRequestMessage(HttpMethod.Delete, url), cancellationToken);

    public async Task<HttpResponseMessage> PatchAsync(string url, HttpContent? content = null, CancellationToken cancellationToken = default)
        => await SendAsync(new HttpRequestMessage(HttpMethod.Patch, url) { Content = content }, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // TokenProvider is in the same Blazor circuit scope - always gets the right instance
        var token = await _tokenProvider.GetTokenAsync();

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _logger.LogDebug("Added Bearer token to {Method} {Url}", request.Method, request.RequestUri?.OriginalString);
        }
        else
        {
            _logger.LogDebug("No token available for {Method} {Url}", request.Method, request.RequestUri?.OriginalString);
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }
}
