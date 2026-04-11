using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LearnLuxembourgish.Shared.Services;

public interface ISproochmaschinnService
{
    Task<(byte[] AudioData, string ContentType)?> GenerateAudioAsync(string luxembourgishText, CancellationToken cancellationToken = default);
}

public class SproochmaschinnService : ISproochmaschinnService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SproochmaschinnService> _logger;

    private const int PollIntervalMs = 500;
    private const int MaxPollAttempts = 30;

    public SproochmaschinnService(HttpClient httpClient, IConfiguration configuration, ILogger<SproochmaschinnService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(byte[] AudioData, string ContentType)?> GenerateAudioAsync(string luxembourgishText, CancellationToken cancellationToken = default)
    {
        var baseUrl = _configuration["Audio:SproochmaschinnUrl"] ?? "https://sproochmaschinn.lu/api";

        try
        {
            // Step 1: create a session
            var sessionResponse = await _httpClient.PostAsync($"{baseUrl}/session", null, cancellationToken);
            sessionResponse.EnsureSuccessStatusCode();

            var session = await sessionResponse.Content.ReadFromJsonAsync<SproochmaschinnSession>(cancellationToken: cancellationToken);
            if (session?.SessionId is null)
            {
                _logger.LogWarning("Failed to obtain a session ID from sproochmaschinn.lu");
                return null;
            }

            // Step 2: submit TTS request
            var ttsRequest = new { text = luxembourgishText, model = "claude" };
            var ttsResponse = await _httpClient.PostAsJsonAsync($"{baseUrl}/tts/{session.SessionId}", ttsRequest, cancellationToken);
            ttsResponse.EnsureSuccessStatusCode();

            var queued = await ttsResponse.Content.ReadFromJsonAsync<SproochmaschinnQueued>(cancellationToken: cancellationToken);
            if (queued?.RequestId is null)
            {
                _logger.LogWarning("TTS response did not contain a request_id");
                return null;
            }

            // Step 3: poll until completed
            SproochmaschinnResult? result = null;
            for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
            {
                await Task.Delay(PollIntervalMs, cancellationToken);

                var pollResponse = await _httpClient.GetAsync($"{baseUrl}/result/{queued.RequestId}", cancellationToken);
                pollResponse.EnsureSuccessStatusCode();

                result = await pollResponse.Content.ReadFromJsonAsync<SproochmaschinnResult>(cancellationToken: cancellationToken);

                if (result?.Status == "completed" || result?.Result?.Data is not null)
                    break;

                if (result?.Status == "failed")
                {
                    _logger.LogWarning("sproochmaschinn TTS processing failed for request {RequestId}", queued.RequestId);
                    return null;
                }

                result = null;
            }

            var audioData = result?.Result?.Data;
            if (audioData is null)
            {
                _logger.LogWarning("sproochmaschinn polling timed out for request {RequestId}", queued?.RequestId);
                return null;
            }

            var format = result!.Result!.Format ?? "wav";
            var bytes = Convert.FromBase64String(audioData);
            return (bytes, $"audio/{format}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "sproochmaschinn audio generation failed for text length {Length}", luxembourgishText.Length);
            return null;
        }
    }

    private sealed class SproochmaschinnSession
    {
        [JsonPropertyName("session_id")]
        public string? SessionId { get; init; }
    }

    private sealed class SproochmaschinnQueued
    {
        [JsonPropertyName("request_id")]
        public string? RequestId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }

    private sealed class SproochmaschinnResult
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("result")]
        public SproochmaschinnAudioResult? Result { get; init; }
    }

    private sealed class SproochmaschinnAudioResult
    {
        [JsonPropertyName("data")]
        public string? Data { get; init; }

        [JsonPropertyName("format")]
        public string? Format { get; init; }
    }
}
