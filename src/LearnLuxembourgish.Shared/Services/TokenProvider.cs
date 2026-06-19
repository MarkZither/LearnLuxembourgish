using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace LearnLuxembourgish.Shared.Services;

/// <summary>
/// Stores the JWT token in server-side memory (scoped per Blazor circuit)
/// Token must be explicitly set by components (cannot auto-load from localStorage in DelegatingHandler context)
/// </summary>
public class TokenProvider
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<TokenProvider> _logger;
    private string? _token;

    public TokenProvider(IJSRuntime jsRuntime, ILogger<TokenProvider> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    /// <summary>
    /// Gets the token from memory (does NOT load from localStorage)
    /// Use TokenInitializer component to preload from localStorage
    /// </summary>
    public Task<string?> GetTokenAsync()
    {
        if (_token != null)
        {
            _logger.LogDebug("Returning token from memory");
        }
        else
        {
            _logger.LogDebug("No token in memory - TokenInitializer may not have run yet");
        }

        return Task.FromResult(_token);
    }

    public async Task SetTokenAsync(string token)
    {
        _logger.LogInformation("Setting token in memory");
        _token = token;

        // Also save to localStorage for persistence across circuits
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "jwt_token", token);
            _logger.LogDebug("Token saved to localStorage");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save token to localStorage - will use in-memory only");
        }
    }

    public Task<bool> HasTokenAsync()
    {
        return Task.FromResult(!string.IsNullOrEmpty(_token));
    }

    public async Task ClearTokenAsync()
    {
        _logger.LogInformation("Clearing token");
        _token = null;

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "jwt_token");
            _logger.LogDebug("Token removed from localStorage");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove token from localStorage");
        }
    }
}
