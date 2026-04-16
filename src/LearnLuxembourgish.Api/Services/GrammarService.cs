using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using LearnLuxembourgish.Api.RateLimiting;
using LearnLuxembourgish.Shared.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace LearnLuxembourgish.Api.Services;

public class GrammarService : IGrammarService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GrammarService> _logger;
    private readonly IOutboundCallBudget _outboundBudget;
    private static readonly ActivitySource ActivitySource = new("LearnLuxembourgish.Grammar");

    private static readonly string SystemPromptBase =
        "You are an expert Luxembourgish (Lëtzebuergesch) language teacher. " +
        "Given an original text and its Luxembourgish translation (produced by DeepL), provide a concise grammatical breakdown and translation review.\n\n" +
        "CRITICAL RULES:\n" +
        "- Be terse. Only mention what is actually present in the translation.\n" +
        "- If a section has nothing meaningful to say, OMIT it entirely — do not write filler like 'no issues found' or 'this rule does not apply here'.\n" +
        "- Do not repeat the full sentence in every section.\n" +
        "- VERB CONJUGATION SELF-CHECK: Before suggesting any change to a verb form, verify it against the conjugation you identified in the Verbs section. " +
        "In Luxembourgish V2 word order, the conjugated verb form does NOT change when it inverts with the subject — " +
        "'Um 7 Auer huelen ech' uses the same 'ech' form as 'ech huelen'; do NOT strip endings to create a bare stem. " +
        "If a form matches standard conjugation for that person/tense, it is correct — do not 'correct' it.\n" +
        "- Only propose a correction if you can name the specific rule it violates AND confirm the replacement form is the standard conjugated form for that person and tense.\n" +
        "- NORTHERN DIALECT VOCABULARY: This app targets northern Luxembourgish. Always apply these replacements in the revised translation: " +
        "'Hond' → 'Mupp' (plural: 'Honden' → 'Muppen'). If DeepL uses 'Hond'/'Honden', correct it to 'Mupp'/'Muppen' and note it in the Translation Review.\n" +
        "- EIFELER REGEL SELF-CHECK: Before writing the revised-translation block, go through EVERY word you analyzed in the Eifeler Regel section, one by one:\n" +
        "  1. Find that word in your revised sentence.\n" +
        "  2. If your analysis said KEEP -n, confirm the word still ends in -n in your revised text.\n" +
        "  3. If your analysis said DROP -n, confirm the -n is removed in your revised text.\n" +
        "  4. If ANY word doesn't match, fix it before outputting the block.\n" +
        "  Example: if you wrote 'ginn dacks → KEEP -n' but your revised sentence says 'gi dacks', that is a contradiction — fix it to 'ginn dacks'.\n\n" +
        "REQUIRED SECTIONS (always include):\n\n" +
        "**Verbs** – For each verb: infinitive, conjugated form used, tense, person, conjugation pattern, irregular forms if any.\n" +
        "After the prose, output a machine-readable block (stripped before display):\n" +
        "```verbs-json\n[{\"infinitive\":\"goen\",\"formUsed\":\"geet\",\"english\":\"to go (on foot)\"}]\n```\n\n" +
        "**Translation Review** – Assess accuracy and naturalness. If the translation is correct and natural, say so in one sentence. " +
        "If corrections are needed, name the specific rule violated and confirm the corrected form is the proper conjugated form. " +
        "IMPORTANT: The revised translation must incorporate ALL findings from previous sections — " +
        "especially the Eifeler Regel. Do not blindly drop -n from verb forms; only drop -n where the Eifeler Regel analysis " +
        "determined the next word starts with a non-UNITED-ZOHA letter. 'ginn dacks' keeps -n because 'd' is in UNITED ZOHA. " +
        "Always end with a machine-readable block containing the final Luxembourgish sentence (revised if needed, or the original DeepL if it was correct):\n" +
        "```revised-translation\nthe final luxembourgish text here\n```\n\n" +
        "OPTIONAL SECTIONS (include only if there is something meaningful to say):\n";

    private static readonly Dictionary<string, string> AspectPrompts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nouns-genders"] =
            "**Nouns & Genders** – List each noun with its gender (masculine/feminine/neuter) and definite article (de/d'/d'/den/dem/des). " +
            "Explain any gender that may surprise English or Polish speakers.",
        ["eifeler-regel"] =
            "**Eifeler Regel (n-rule)** – This rule governs whether a word-final -n is kept or dropped.\n\n" +
            "The candidate word pairs have been pre-computed and provided in the user message under 'EIFELER REGEL CANDIDATES'. " +
            "Analyze ONLY those pairs — do NOT add any other words. " +
            "For each pair, check the first letter of the second word against **UNITED ZOHA** (U, N, I, T, E, D, Z, O, H, A). " +
            "If it's in UNITED ZOHA → KEEP the -n. If it's not → DROP the -n.\n\n" +
            "Output one line per pair in this exact format:\n" +
            "  'word1' + 'word2' → first letter 'X' → in/not in UNITED ZOHA → KEEP/DROP → 'result'\n\n" +
            "EXAMPLES:\n" +
            "  • 'ginn' + 'dacks' → first letter 'd' → in UNITED ZOHA → KEEP → 'ginn dacks'\n" +
            "  • 'ginn' + 'gären' → first letter 'g' → NOT in UNITED ZOHA → DROP → 'gi gären'\n" +
            "  • 'kommen' + 'mat' → first letter 'm' → NOT in UNITED ZOHA → DROP → 'komme mat'\n\n" +
            "If no candidates are provided, omit this section entirely.",
        ["inversion"] =
            "**Inversion Rule (V2 Word Order)** – Identify any fronted elements (adverbs, time expressions, objects, prepositional phrases). " +
            "For each, show whether the verb and subject correctly invert as required by Luxembourgish V2 word order (i.e. the finite verb must remain in second position). " +
            "Flag any missing or incorrect inversion in the translation.",
        ["verbs-of-motion"] =
            "**Verbs of Motion** – List only the verbs of motion that are actually present. For each, confirm whether the correct verb is used " +
            "(fueren: vehicle/train/bus; fléien: flying; goen/ginn: walking; schwammen: swimming; reeden: cycling). " +
            "Do NOT write sentences about verbs that are absent. If only one motion verb is present, list only that one.",
        ["other-grammar"] =
            "**Other Grammar Notes** – Cover case usage, prepositions, word order, or idiomatic expressions as needed.",
    };

    /// <summary>All aspect keys in default display order.</summary>
    private static readonly IReadOnlyList<string> AllAspectKeys =
        ["nouns-genders", "eifeler-regel", "inversion", "verbs-of-motion", "other-grammar"];

    private static string BuildSystemPrompt(IEnumerable<string>? grammarAspects)
    {
        var keys = grammarAspects?.Where(k => AspectPrompts.ContainsKey(k)).ToList();
        var effectiveKeys = (keys is { Count: > 0 }) ? keys : AllAspectKeys;

        var sections = effectiveKeys
            .Select((k, i) => $"{i + 1}. {AspectPrompts[k]}")
            .ToList();

        return SystemPromptBase +
            string.Join("\n", sections) +
            "\n\nFormat each included section with a clear heading. Be concise and educational, suitable for an intermediate language learner.";
    }

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

    public GrammarService(IConfiguration configuration, ILogger<GrammarService> logger, IOutboundCallBudget outboundBudget)
    {
        _configuration = configuration;
        _logger = logger;
        _outboundBudget = outboundBudget;
    }

    /// <summary>
    /// Pre-computes Eifeler Regel candidate pairs: words ending in 'n' paired with the immediately following word.
    /// </summary>
    internal static List<(string Word, string NextWord)> GetEifelerRegelCandidates(string text)
    {
        // Split on whitespace, strip trailing punctuation for the ends-in-n check but keep original for display
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var results = new List<(string Word, string NextWord)>();

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var word = tokens[i].TrimEnd(',', '.', '!', '?', ';', ':');
            if (word.Length > 0 && word[^1] is 'n' or 'N')
            {
                var nextWord = tokens[i + 1].TrimEnd(',', '.', '!', '?', ';', ':');
                results.Add((word, nextWord));
            }
        }

        return results;
    }

    public async Task<string?> ExplainGrammarAsync(
        string sourceText,
        string translatedText,
        string? apiKey = null,
        string? provider = null,
        IEnumerable<string>? grammarAspects = null,
        CancellationToken cancellationToken = default)
    {
        // Budget check must be outside the try/catch so the exception propagates to the controller.
        var budgetResult = await _outboundBudget.TryConsumeAsync(cancellationToken);
        if (!budgetResult.Allowed)
            throw new OutboundBudgetExceededException(budgetResult);

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
            history.AddSystemMessage(BuildSystemPrompt(grammarAspects));
            var eifelerCandidates = "";
            var aspectList = grammarAspects?.ToList();
            if (aspectList is null || aspectList.Count == 0 || aspectList.Contains("eifeler-regel", StringComparer.OrdinalIgnoreCase))
            {
                var pairs = GetEifelerRegelCandidates(translatedText);
                if (pairs.Count > 0)
                {
                    eifelerCandidates = "\n\nEIFELER REGEL CANDIDATES (pre-computed — only these words end in 'n'):\n" +
                        string.Join("\n", pairs.Select(p => $"  '{p.Word}' + '{p.NextWord}'"));
                }
                else
                {
                    eifelerCandidates = "\n\nEIFELER REGEL CANDIDATES: none (no words ending in 'n' found).";
                }
            }

            history.AddUserMessage(
                $"Original text ({(sourceText.Length > 0 ? "source" : "unknown")}): {sourceText}" +
                $"\n\nLuxembourgish translation (from DeepL): {translatedText}" +
                eifelerCandidates +
                $"\n\nPlease provide the grammatical breakdown for the selected sections.");

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

        // Budget check must be outside the try/catch so the exception propagates to the caller.
        var budgetResult = await _outboundBudget.TryConsumeAsync(cancellationToken);
        if (!budgetResult.Allowed)
            throw new OutboundBudgetExceededException(budgetResult);

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
