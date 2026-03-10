using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LearnLuxembourgish.Api.Services;

public class AudioService : IAudioService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AudioService> _logger;
    private readonly HttpClient _httpClient;
    private static readonly ActivitySource ActivitySource = new("LearnLuxembourgish.Audio");

    private const int PollIntervalMs = 500;
    private const int MaxPollAttempts = 30; // 15 seconds max

    public AudioService(IConfiguration configuration, ILogger<AudioService> logger, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("audio");
    }

    public async Task<(string? Url, string? Error)> GenerateAudioAsync(string luxembourgishText, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GenerateAudio");
        activity?.SetTag("text.length", luxembourgishText.Length);
        var startTime = Stopwatch.GetTimestamp();

        try
        {
            var baseUrl = _configuration["Audio:SproochmaschinnUrl"] ?? "https://sproochmaschinn.lu/api";

            // Step 1: create a session
            _logger.LogInformation("Creating sproochmaschinn.lu session");
            var sessionResponse = await _httpClient.PostAsync($"{baseUrl}/session", null, cancellationToken);
            sessionResponse.EnsureSuccessStatusCode();

            var session = await sessionResponse.Content.ReadFromJsonAsync<SproochmaschinnSession>(cancellationToken: cancellationToken);
            if (session?.SessionId is null)
            {
                _logger.LogWarning("Failed to obtain a session ID from sproochmaschinn.lu");
                return (null, "Could not create a sproochmaschinn.lu session.");
            }

            // Step 2: submit TTS request — returns {request_id, status: "queued"}
            _logger.LogInformation("Submitting TTS request for text length {Length}", luxembourgishText.Length);
            var ttsRequest = new { text = luxembourgishText, model = "claude" };
            var ttsResponse = await _httpClient.PostAsJsonAsync($"{baseUrl}/tts/{session.SessionId}", ttsRequest, cancellationToken);
            ttsResponse.EnsureSuccessStatusCode();

            var queued = await ttsResponse.Content.ReadFromJsonAsync<SproochmaschinnQueued>(cancellationToken: cancellationToken);
            if (queued?.RequestId is null)
            {
                _logger.LogWarning("TTS response did not contain a request_id");
                return (null, "TTS request did not return a request ID.");
            }

            _logger.LogInformation("TTS queued with request_id {RequestId}", queued.RequestId);

            // Step 3: poll GET /api/result/{request_id} until status == "completed"
            SproochmaschinnResult? result = null;
            for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
            {
                await Task.Delay(PollIntervalMs, cancellationToken);

                var pollResponse = await _httpClient.GetAsync($"{baseUrl}/result/{queued.RequestId}", cancellationToken);
                pollResponse.EnsureSuccessStatusCode();

                result = await pollResponse.Content.ReadFromJsonAsync<SproochmaschinnResult>(cancellationToken: cancellationToken);

                _logger.LogDebug("Poll attempt {Attempt}: status={Status}", attempt + 1, result?.Status);

                if (result?.Status == "completed" || result?.Result?.Data is not null)
                    break;

                if (result?.Status == "failed")
                    return (null, $"TTS processing failed on the server.");

                result = null;
            }

            var audioData = result?.Result?.Data;
            if (audioData is null)
            {
                _logger.LogWarning("TTS polling timed out or returned no audio for request {RequestId}", queued.RequestId);
                return (null, "Audio generation timed out.");
            }

            var elapsed = Stopwatch.GetElapsedTime(startTime);
            var format = result!.Result!.Format ?? "wav";
            var duration = result.Result.Duration;
            _logger.LogInformation("Audio generation completed in {ElapsedMs}ms (duration={Duration}s, format={Format})",
                elapsed.TotalMilliseconds, duration, format);
            activity?.SetTag("duration.ms", elapsed.TotalMilliseconds);
            activity?.SetTag("audio.duration_s", duration);
            activity?.SetTag("success", true);

            return ($"data:audio/{format};base64,{audioData}", null);
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            _logger.LogWarning(ex, "Audio generation failed after {ElapsedMs}ms for text: {Text}", elapsed.TotalMilliseconds, luxembourgishText);
            activity?.SetTag("success", false);
            activity?.SetTag("error", ex.Message);
            return (null, ex.Message);
        }
    }

    private sealed class SproochmaschinnSession
    {
        [JsonPropertyName("session_id")]
        public string? SessionId { get; init; }

        [JsonPropertyName("session_hash")]
        public string? SessionHash { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }

    private sealed class SproochmaschinnQueued
    {
        [JsonPropertyName("request_id")]
        public string? RequestId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }

    private sealed class SproochmaschinnResult
    {
        [JsonPropertyName("request_id")]
        public string? RequestId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("result")]
        public SproochmaschinnAudioResult? Result { get; init; }
    }

    private sealed class SproochmaschinnAudioResult
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("data")]
        public string? Data { get; init; }

        [JsonPropertyName("format")]
        public string? Format { get; init; }

        [JsonPropertyName("duration")]
        public double Duration { get; init; }

        [JsonPropertyName("filename")]
        public string? Filename { get; init; }

        [JsonPropertyName("sample_rate")]
        public int SampleRate { get; init; }
    }
}
