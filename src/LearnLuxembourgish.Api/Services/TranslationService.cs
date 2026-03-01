using DeepL;
using LearnLuxembourgish.Shared.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace LearnLuxembourgish.Api.Services;

public class TranslationService : ITranslationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TranslationService> _logger;
    private readonly HttpClient _httpClient;

    public TranslationService(IConfiguration configuration, ILogger<TranslationService> logger, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("translation");
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        // Try DeepL first
        var deeplApiKey = _configuration["Translation:DeepL:ApiKey"];
        if (!string.IsNullOrEmpty(deeplApiKey))
        {
            try
            {
                return await TranslateWithDeepLAsync(request, deeplApiKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DeepL translation failed, falling back to Mistral");
            }
        }

        // Try Mistral
        var mistralApiKey = _configuration["Translation:Mistral:ApiKey"];
        if (!string.IsNullOrEmpty(mistralApiKey))
        {
            try
            {
                return await TranslateWithMistralAsync(request, mistralApiKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mistral translation failed, falling back to Ollama");
            }
        }

        // Fallback to Ollama / OpenAI-compatible local API
        return await TranslateWithOllamaAsync(request, cancellationToken);
    }

    private async Task<TranslationResult> TranslateWithDeepLAsync(TranslationRequest request, string apiKey, CancellationToken cancellationToken)
    {
        var translator = new Translator(apiKey);
        var sourceCode = request.SourceLanguage.ToUpperInvariant() == "PL" ? LanguageCode.Polish : LanguageCode.English;
        var result = await translator.TranslateTextAsync(request.Text, sourceCode, "LB", cancellationToken: cancellationToken);
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
        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion("mistral-large-latest", new Uri("https://api.mistral.ai/v1"), apiKey)
            .Build();

        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage("You are an expert Luxembourgish translator. Translate the provided text idiomatically to Luxembourgish (Lëtzebuergesch). Return only the translation, no explanations.");
        history.AddUserMessage($"Translate this {request.SourceLanguage} text to Luxembourgish: {request.Text}");

        var response = await chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
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
        var ollamaEndpoint = _configuration["Translation:Ollama:Endpoint"] ?? "http://localhost:11434/v1";
        var ollamaModel = _configuration["Translation:Ollama:Model"] ?? "llama3";

        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(ollamaModel, new Uri(ollamaEndpoint), "ollama")
            .Build();

        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage("You are an expert Luxembourgish translator. Translate the provided text idiomatically to Luxembourgish (Lëtzebuergesch). Return only the translation, no explanations.");
        history.AddUserMessage($"Translate this {request.SourceLanguage} text to Luxembourgish: {request.Text}");

        var response = await chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
        return new TranslationResult
        {
            OriginalText = request.Text,
            SourceLanguage = request.SourceLanguage,
            TranslatedText = response.Content ?? string.Empty,
            Provider = "Ollama"
        };
    }
}
