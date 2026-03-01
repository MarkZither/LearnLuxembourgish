using System.Text.Json;

namespace LearnLuxembourgish.Api.Services;

public class AudioService : IAudioService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AudioService> _logger;
    private readonly HttpClient _httpClient;

    public AudioService(IConfiguration configuration, ILogger<AudioService> logger, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("audio");
    }

    public async Task<string?> GenerateAudioAsync(string luxembourgishText, CancellationToken cancellationToken = default)
    {
        try
        {
            var baseUrl = _configuration["Audio:SproochmaschinnUrl"] ?? "https://sproochmaschinn.lu/api";
            var requestUrl = $"{baseUrl}/tts?text={Uri.EscapeDataString(luxembourgishText)}&lang=lb";

            var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonSerializer.Deserialize<JsonElement>(content);

            if (json.TryGetProperty("audioUrl", out var audioUrlProp))
            {
                return audioUrlProp.GetString();
            }

            return requestUrl;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio generation failed for text: {Text}", luxembourgishText);
            return null;
        }
    }
}
