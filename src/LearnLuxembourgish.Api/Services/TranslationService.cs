using DeepL;
using LearnLuxembourgish.Shared.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Diagnostics;

namespace LearnLuxembourgish.Api.Services;

public class TranslationService : ITranslationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TranslationService> _logger;
    private readonly HttpClient _httpClient;
    private static readonly ActivitySource ActivitySource = new("LearnLuxembourgish.Translation");

    public TranslationService(IConfiguration configuration, ILogger<TranslationService> logger, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("translation");
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("TranslateAsync");
        activity?.SetTag("source.language", request.SourceLanguage);
        activity?.SetTag("text.length", request.Text.Length);

        _logger.LogInformation("Starting translation process for {SourceLanguage} text with length {Length}", 
            request.SourceLanguage, request.Text.Length);

        // Try DeepL first
        var deeplApiKey = _configuration["Translation:DeepL:ApiKey"];
        if (!string.IsNullOrEmpty(deeplApiKey))
        {
            try
            {
                _logger.LogInformation("Attempting translation with DeepL");
                var result = await TranslateWithDeepLAsync(request, deeplApiKey, cancellationToken);
                _logger.LogInformation("Successfully translated with DeepL");
                activity?.SetTag("provider", "DeepL");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DeepL translation failed, falling back to Mistral");
                activity?.AddEvent(new ActivityEvent("DeepL failed, falling back"));
            }
        }

        // Try Mistral
        var mistralApiKey = _configuration["Translation:Mistral:ApiKey"];
        if (!string.IsNullOrEmpty(mistralApiKey))
        {
            try
            {
                _logger.LogInformation("Attempting translation with Mistral");
                var result = await TranslateWithMistralAsync(request, mistralApiKey, cancellationToken);
                _logger.LogInformation("Successfully translated with Mistral");
                activity?.SetTag("provider", "Mistral");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mistral translation failed, falling back to Ollama");
                activity?.AddEvent(new ActivityEvent("Mistral failed, falling back"));
            }
        }

        // Fallback to Ollama / OpenAI-compatible local API
        _logger.LogInformation("Attempting translation with Ollama");
        var ollamaResult = await TranslateWithOllamaAsync(request, cancellationToken);
        _logger.LogInformation("Successfully translated with Ollama");
        activity?.SetTag("provider", "Ollama");
        return ollamaResult;
    }

    private async Task<TranslationResult> TranslateWithDeepLAsync(TranslationRequest request, string apiKey, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("TranslateWithDeepL");
        var startTime = Stopwatch.GetTimestamp();

        var translator = new Translator(apiKey);
        var sourceCode = request.SourceLanguage.ToUpperInvariant() == "PL" ? LanguageCode.Polish : LanguageCode.English;

        _logger.LogDebug("Calling DeepL API for translation");
        var result = await translator.TranslateTextAsync(request.Text, sourceCode, "LB", cancellationToken: cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(startTime);
        _logger.LogInformation("DeepL translation completed in {ElapsedMs}ms", elapsed.TotalMilliseconds);
        activity?.SetTag("duration.ms", elapsed.TotalMilliseconds);

        return new TranslationResult
        {
            OriginalText = request.Text,
            SourceLanguage = request.SourceLanguage,
            TranslatedText = result.Text,
            Provider = "DeepL"
        };
    }

    private async Task<TranslationResult> TranslateWithMistralAsync(TranslationRequest request, string apiKey, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("TranslateWithMistral");
        var startTime = Stopwatch.GetTimestamp();

        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion("mistral-large-latest", new Uri("https://api.mistral.ai/v1"), apiKey)
            .Build();

        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage("You are an expert Luxembourgish translator. Translate the provided text idiomatically to Luxembourgish (Lëtzebuergesch). Return only the translation, no explanations.");
        history.AddUserMessage($"Translate this {request.SourceLanguage} text to Luxembourgish: {request.Text}");

        _logger.LogDebug("Calling Mistral API for translation");
        var response = await chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(startTime);
        _logger.LogInformation("Mistral translation completed in {ElapsedMs}ms", elapsed.TotalMilliseconds);
        activity?.SetTag("duration.ms", elapsed.TotalMilliseconds);

        return new TranslationResult
        {
            OriginalText = request.Text,
            SourceLanguage = request.SourceLanguage,
            TranslatedText = response.Content ?? string.Empty,
            Provider = "Mistral"
        };
    }

    private async Task<TranslationResult> TranslateWithOllamaAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("TranslateWithOllama");
        var startTime = Stopwatch.GetTimestamp();

        var ollamaEndpoint = _configuration["Translation:Ollama:Endpoint"] ?? "http://localhost:11434/v1";
        var ollamaModel = _configuration["Translation:Ollama:Model"] ?? "llama3";

        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(ollamaModel, new Uri(ollamaEndpoint), "ollama")
            .Build();

        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage("You are an expert Luxembourgish translator. Translate the provided text idiomatically to Luxembourgish (Lëtzebuergesch). Return only the translation, no explanations.");
        history.AddUserMessage($"Translate this {request.SourceLanguage} text to Luxembourgish: {request.Text}");

        _logger.LogDebug("Calling Ollama API for translation");
        var response = await chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(startTime);
        _logger.LogInformation("Ollama translation completed in {ElapsedMs}ms", elapsed.TotalMilliseconds);
        activity?.SetTag("duration.ms", elapsed.TotalMilliseconds);

        return new TranslationResult
        {
            OriginalText = request.Text,
            SourceLanguage = request.SourceLanguage,
            TranslatedText = response.Content ?? string.Empty,
            Provider = "Ollama"
        };
    }
}
