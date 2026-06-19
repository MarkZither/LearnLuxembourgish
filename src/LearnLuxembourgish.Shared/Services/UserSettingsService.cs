using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace LearnLuxembourgish.Shared.Services;

/// <summary>
/// Manages per-user settings persisted in browser localStorage.
/// Scoped per Blazor circuit (same lifetime as the SignalR connection).
/// </summary>
public class UserSettingsService
{
    private readonly IJSRuntime _js;
    private readonly ILogger<UserSettingsService> _logger;

    // localStorage key constants
    private const string KeyGrammarProvider = "settings_grammar_provider";
    private const string KeyMistralApiKey   = "settings_mistral_api_key";
    private const string KeyGroqApiKey      = "settings_groq_api_key";

    // In-memory cache so we don't hit localStorage on every call within a circuit
    private string? _cachedProvider;
    private string? _cachedMistralApiKey;
    private string? _cachedGroqApiKey;
    private bool _loaded = false;

    public UserSettingsService(IJSRuntime js, ILogger<UserSettingsService> logger)
    {
        _js = js;
        _logger = logger;
    }

    /// <summary>
    /// Loads settings from localStorage into the in-memory cache.
    /// Must be called after JS interop is available (i.e., after first render).
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            _cachedProvider      = await _js.InvokeAsync<string?>("localStorage.getItem", KeyGrammarProvider);
            _cachedMistralApiKey = await _js.InvokeAsync<string?>("localStorage.getItem", KeyMistralApiKey);
            _cachedGroqApiKey    = await _js.InvokeAsync<string?>("localStorage.getItem", KeyGroqApiKey);
            _loaded = true;
            _logger.LogDebug("User settings loaded. Provider={Provider}, HasMistralKey={HasMistralKey}, HasGroqKey={HasGroqKey}",
                _cachedProvider, !string.IsNullOrEmpty(_cachedMistralApiKey), !string.IsNullOrEmpty(_cachedGroqApiKey));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load user settings from localStorage");
        }
    }

    /// <summary>
    /// Grammar provider preference: "mistral" or "local".
    /// Returns "mistral" as default when a key is configured.
    /// </summary>
    public string GrammarProvider
    {
        get => _cachedProvider ?? "mistral";
        set => _cachedProvider = value;
    }

    /// <summary>
    /// Mistral API key provided by the user via the Settings screen.
    /// Null/empty means fall back to the server-configured key (or local model).
    /// </summary>
    public string? MistralApiKey
    {
        get => string.IsNullOrWhiteSpace(_cachedMistralApiKey) ? null : _cachedMistralApiKey;
        set => _cachedMistralApiKey = value;
    }

    /// <summary>
    /// Groq API key provided by the user via the Settings screen.
    /// Null/empty means fall back to the server-configured key (or local model).
    /// </summary>
    public string? GroqApiKey
    {
        get => string.IsNullOrWhiteSpace(_cachedGroqApiKey) ? null : _cachedGroqApiKey;
        set => _cachedGroqApiKey = value;
    }

    public bool IsLoaded => _loaded;

    /// <summary>
    /// Saves current settings to localStorage.
    /// </summary>
    public async Task SaveAsync()
    {
        try
        {
            await SetOrRemoveAsync(KeyGrammarProvider, _cachedProvider);
            await SetOrRemoveAsync(KeyMistralApiKey, _cachedMistralApiKey);
            await SetOrRemoveAsync(KeyGroqApiKey, _cachedGroqApiKey);
            _logger.LogInformation("User settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save user settings to localStorage");
            throw;
        }
    }

    private async Task SetOrRemoveAsync(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
            await _js.InvokeVoidAsync("localStorage.removeItem", key);
        else
            await _js.InvokeVoidAsync("localStorage.setItem", key, value);
    }
}
