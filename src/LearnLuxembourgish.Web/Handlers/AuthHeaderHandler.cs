using LearnLuxembourgish.Shared.Services;

namespace LearnLuxembourgish.Web.Handlers;

public class AuthHeaderHandler : DelegatingHandler
{
    private readonly TokenProvider _tokenProvider;
    private readonly ILogger<AuthHeaderHandler> _logger;

    public AuthHeaderHandler(TokenProvider tokenProvider, ILogger<AuthHeaderHandler> logger)
    {
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Get token from TokenProvider (will auto-restore from localStorage if needed)
        var token = await _tokenProvider.GetTokenAsync();

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            _logger.LogDebug("Added Authorization header to request for {Path}", request.RequestUri?.PathAndQuery);
        }
        else
        {
            _logger.LogDebug("No JWT token available for request to {Path}", request.RequestUri?.PathAndQuery);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
