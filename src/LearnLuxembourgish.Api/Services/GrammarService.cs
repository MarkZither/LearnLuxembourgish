using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using LearnLuxembourgish.Shared.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace LearnLuxembourgish.Api.Services;

public class GrammarService : IGrammarService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GrammarService> _logger;
    private static readonly ActivitySource ActivitySource = new("LearnLuxembourgish.Grammar");

    private const string SystemPrompt =
        "You are an expert Luxembourgish (Lëtzebuergesch) language teacher. " +
        "Given an original text and its Luxembourgish translation (produced by DeepL), provide a structured grammatical breakdown and translation review. " +
        "Your response must cover the following sections:\n" +
        "1. **Nouns & Genders** – List each noun with its gender (masculine/feminine/neuter) and definite article (de/d'/d'/den/dem/des). " +
        "Explain any gender that may surprise English or Polish speakers.\n" +
        "2. **Verbs** – Identify every verb. For each, state its infinitive form, the exact conjugated form used in the sentence, tense, conjugation pattern, and any irregular forms. " +
        "After the prose, output a fenced code block tagged `verbs-json` (for programmatic extraction) listing every verb found:\n" +
        "```verbs-json\n[{\"infinitive\":\"goen\",\"formUsed\":\"geet\",\"english\":\"to go (on foot)\"}]\n```\n" +
        "3. **Eifeler Regel** – Explain where the Eifeler Regel applies in the translation: " +
        "specifically where a word-final -n is dropped or added before a following consonant or vowel. " +
        "Give the affected words and the rule that governs them.\n" +
        "4. **Inversion Rule (V2 Word Order)** – Identify any fronted elements (adverbs, time expressions, objects, prepositional phrases). " +
        "For each, show whether the verb and subject correctly invert as required by Luxembourgish V2 word order (i.e. the finite verb must remain in second position). " +
        "Flag any missing or incorrect inversion in the translation.\n" +
        "5. **Verbs of Motion** – List every verb of motion in the translation. For each, confirm whether the correct Luxembourgish verb is used for the mode of transport or movement " +
        "(e.g., fueren for vehicle/train/bus travel, fléien for flying, goen/ginn for walking on foot, schwammen for swimming, reeden for cycling, fueren/kommen for general directed motion). " +
        "Flag any where a different verb would be more natural or correct.\n" +
        "6. **Other Grammar Notes** – Cover case usage, prepositions, word order, or idiomatic expressions as needed.\n" +
        "7. **Translation Review** – Using the grammar rules above, assess whether the DeepL translation is accurate and natural. " +
        "Highlight any errors, awkward phrasings, or improvements. If corrections are needed, provide a revised version with a brief explanation.\n" +
        "Format each section with a clear heading. Be concise but educational, suitable for an intermediate language learner.";

    private const string ConjugationSystemPrompt =
        "You are an expert Luxembourgish grammar reference. " +
        "Given a JSON array of verb infinitives, return a complete conjugation table for each verb. " +
        "Output ONLY valid JSON — no markdown fences, no prose, no extra text whatsoever. " +
        "Structure: a JSON array of objects, each with: " +
        "\"infinitive\" (string), \"english\" (brief English gloss, e.g. \"to go (on foot)\"), \"tenses\" (array of tense objects). " +
        "Each tense object has: \"name\" (tense name with English in parentheses) and " +
        "\"forms\" (object with exactly these keys: \"ech\", \"du\", \"hien/si/es\", \"mir\", \"dir\", \"si\"). " +
        "Include exactly these tenses for every verb: \"Präsens (Present)\", \"Perfekt (Present Perfect)\", \"Futur I (Future)\". " +
        "Use the correct auxiliary verb + past participle for Perfekt (sinn for motion verbs, hunn for transitive verbs).";

    public GrammarService(IConfiguration configuration, ILogger<GrammarService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> ExplainGrammarAsync(
        string sourceText,
        string translatedText,
        string? apiKey = null,
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("ExplainGrammar");
        activity?.SetTag("source.length", sourceText.Length);
        activity?.SetTag("translated.length", translatedText.Length);
        var startTime = Stopwatch.GetTimestamp();

        try
        {
            _logger.LogInformation("Generating grammar explanation for translation");
            var (kernel, resolvedProvider) = BuildKernel(apiKey, provider);
            activity?.SetTag("provider", resolvedProvider);

            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(SystemPrompt);
            history.AddUserMessage(
                $"Original text ({(sourceText.Length > 0 ? "source" : "unknown")}): {sourceText}" +
                $"\n\nLuxembourgish translation (from DeepL): {translatedText}" +
                $"\n\nPlease provide the full grammatical breakdown and translation review.");

            var response = await chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);

            var elapsed = Stopwatch.GetElapsedTime(startTime);
            _logger.LogInformation("Grammar explanation completed in {ElapsedMs}ms via {Provider}",
                elapsed.TotalMilliseconds, resolvedProvider);
            activity?.SetTag("duration.ms", elapsed.TotalMilliseconds);
            activity?.SetTag("success", true);

            return response.Content;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            _logger.LogWarning(ex, "Grammar explanation failed after {ElapsedMs}ms", elapsed.TotalMilliseconds);
            activity?.SetTag("success", false);
            activity?.SetTag("error", ex.Message);
            return null;
        }
    }

    public async Task<List<VerbConjugationTable>?> ConjugateVerbsAsync(
        IEnumerable<string> infinitives,
        string? apiKey = null,
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        var verbList = infinitives.Distinct().ToList();
        if (verbList.Count == 0) return null;

        using var activity = ActivitySource.StartActivity("ConjugateVerbs");
        activity?.SetTag("verb.count", verbList.Count);
        var startTime = Stopwatch.GetTimestamp();

        try
        {
            _logger.LogInformation("Generating conjugation tables for {Count} verbs", verbList.Count);
            var (kernel, resolvedProvider) = BuildKernel(apiKey, provider);
            activity?.SetTag("provider", resolvedProvider);

            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(ConjugationSystemPrompt);
            history.AddUserMessage(JsonSerializer.Serialize(verbList));

            var response = await chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
            var json = ExtractJsonArray(response.Content ?? string.Empty);

            var result = JsonSerializer.Deserialize<List<VerbConjugationTable>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var elapsed = Stopwatch.GetElapsedTime(startTime);
            _logger.LogInformation("Conjugation tables completed in {ElapsedMs}ms via {Provider}",
                elapsed.TotalMilliseconds, resolvedProvider);
            activity?.SetTag("success", true);
            return result;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            _logger.LogWarning(ex, "Conjugation tables failed after {ElapsedMs}ms", elapsed.TotalMilliseconds);
            activity?.SetTag("success", false);
            return null;
        }
    }

    private static string ExtractJsonArray(string content)
    {
        content = content.Trim();
        var fenceMatch = Regex.Match(content, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.Singleline);
        if (fenceMatch.Success)
            content = fenceMatch.Groups[1].Value.Trim();
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }

    /// <summary>
    /// Resolves the kernel to use. Priority order:
    ///   1. Explicit per-request apiKey (from UI settings) with provider hint (groq / mistral)
    ///   2. Groq API key from configuration (user secrets / env vars)
    ///   3. Mistral API key from configuration (user secrets / env vars)
    ///   4. Local Ollama model (fallback)
    /// </summary>
    private (Kernel kernel, string provider) BuildKernel(string? requestApiKey, string? requestProvider)
    {
        var resolvedProvider = (requestProvider ?? string.Empty).Trim().ToLowerInvariant();

        // 1. Per-request key wins (UI-provided, stored per session)
        if (!string.IsNullOrEmpty(requestApiKey) && resolvedProvider != "local")
        {
            if (resolvedProvider == "groq")
            {
                var groqModel = _configuration["Grammar:Groq:Model"] ?? "llama-3.3-70b-versatile";
                _logger.LogDebug("Using Groq (per-request key) model {Model}", groqModel);
                return (
                    Kernel.CreateBuilder()
                        .AddOpenAIChatCompletion(groqModel, new Uri("https://api.groq.com/openai/v1"), requestApiKey)
                        .Build(),
                    "groq"
                );
            }
            else
            {
                var mistralModel = _configuration["Grammar:Mistral:Model"] ?? "mistral-large-latest";
                _logger.LogDebug("Using Mistral (per-request key) model {Model}", mistralModel);
                return (
                    Kernel.CreateBuilder()
                        .AddOpenAIChatCompletion(mistralModel, new Uri("https://api.mistral.ai/v1"), requestApiKey)
                        .Build(),
                    "mistral"
                );
            }
        }

        // 2. Configured Groq key (user secrets / appsettings)
        var configGroqKey = _configuration["Grammar:Groq:ApiKey"];
        if (!string.IsNullOrEmpty(configGroqKey) && resolvedProvider != "local" && resolvedProvider != "mistral")
        {
            var groqModel = _configuration["Grammar:Groq:Model"] ?? "llama-3.3-70b-versatile";
            _logger.LogDebug("Using Groq (config key) model {Model}", groqModel);
            return (
                Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion(groqModel, new Uri("https://api.groq.com/openai/v1"), configGroqKey)
                    .Build(),
                "groq"
            );
        }

        // 3. Configured Mistral key (user secrets / appsettings)
        var configMistralKey = _configuration["Grammar:Mistral:ApiKey"];
        if (!string.IsNullOrEmpty(configMistralKey) && resolvedProvider != "local" && resolvedProvider != "groq")
        {
            var mistralModel = _configuration["Grammar:Mistral:Model"] ?? "mistral-large-latest";
            _logger.LogDebug("Using Mistral (config key) model {Model}", mistralModel);
            return (
                Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion(mistralModel, new Uri("https://api.mistral.ai/v1"), configMistralKey)
                    .Build(),
                "mistral"
            );
        }

        // 4. Local model via Ollama (OpenAI-compatible endpoint)
        var ollamaEndpoint = _configuration["Grammar:Ollama:Endpoint"] ?? "http://localhost:11434/v1";
        var ollamaModel = _configuration["Grammar:Ollama:Model"] ?? "llama3";
        _logger.LogDebug("Using local model {Model} at {Endpoint}", ollamaModel, ollamaEndpoint);
        return (
            Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(ollamaModel, new Uri(ollamaEndpoint), "ollama")
                .Build(),
            "local"
        );
    }
}
