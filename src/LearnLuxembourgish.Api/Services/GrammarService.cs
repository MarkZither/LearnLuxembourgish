using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace LearnLuxembourgish.Api.Services;

public class GrammarService : IGrammarService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GrammarService> _logger;

    public GrammarService(IConfiguration configuration, ILogger<GrammarService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> ExplainGrammarAsync(string sourceText, string translatedText, CancellationToken cancellationToken = default)
    {
        try
        {
            var kernel = BuildKernel();
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(
                "You are a Luxembourgish language expert. Provide a concise grammatical breakdown of the Luxembourgish translation, " +
                "explaining key grammar points such as verb conjugation, noun genders, case usage, and any idiomatic expressions. " +
                "Format the response in a clear, educational way suitable for language learners.");
            history.AddUserMessage(
                $"Original text: {sourceText}\n\nLuxembourgish translation: {translatedText}\n\nPlease explain the grammar.");

            var response = await chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
            return response.Content;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Grammar explanation failed");
            return null;
        }
    }

    private Kernel BuildKernel()
    {
        var mistralApiKey = _configuration["Grammar:Mistral:ApiKey"];
        if (!string.IsNullOrEmpty(mistralApiKey))
        {
            return Kernel.CreateBuilder()
                .AddOpenAIChatCompletion("mistral-large-latest", new Uri("https://api.mistral.ai/v1"), mistralApiKey)
                .Build();
        }

        var ollamaEndpoint = _configuration["Grammar:Ollama:Endpoint"] ?? "http://localhost:11434/v1";
        var ollamaModel = _configuration["Grammar:Ollama:Model"] ?? "llama3";
        return Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(ollamaModel, new Uri(ollamaEndpoint), "ollama")
            .Build();
    }
}
