namespace LearnLuxembourgish.Shared.Models;

public class TranslationRequest
{
    public required string Text { get; set; }
    public required string SourceLanguage { get; set; }

    /// <summary>
    /// Optional Mistral API key supplied by the client (from UI settings, stored per session).
    /// When present, takes priority over the server-configured key.
    /// </summary>
    public string? GrammarApiKey { get; set; }

    /// <summary>
    /// Grammar provider preference: "mistral", "groq", or "local".
    /// Defaults to "mistral" when a Mistral key is present, "groq" when a Groq key is present, otherwise "local".
    /// </summary>
    public string? GrammarProvider { get; set; }
}

public class TranslationResult
{
    public Guid? TranslationId { get; set; }  // Database ID, set when user is authenticated
    public required string OriginalText { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TranslatedText { get; set; }
    public string? AudioUrl { get; set; }
    public string? AudioError { get; set; }
    public string? GrammarExplanation { get; set; }
    public required string Provider { get; set; }
    public List<VerbConjugationTable>? VerbConjugations { get; set; }
}

public class TranslationProgress
{
    public string CurrentStep { get; set; } = string.Empty;
    public int PercentComplete { get; set; }
    public List<string> CompletedSteps { get; set; } = new();
}

public class FlashCardRequest
{
    public required Guid TranslationId { get; set; }
    public required string DeckName { get; set; }
}

public class FlashCardDto
{
    public Guid Id { get; set; }
    public Guid TranslationId { get; set; }
    public string? SourceText { get; set; }
    public string? TranslatedText { get; set; }
    public string? AudioUrl { get; set; }
    public string? GrammarExplanation { get; set; }
    public required string DeckName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public int ReviewCount { get; set; }
}

public class VerbConjugationTable
{
    public required string Infinitive { get; set; }
    public required string English { get; set; }
    public List<VerbTenseTable> Tenses { get; set; } = [];
}

public class VerbTenseTable
{
    public required string Name { get; set; }
    public Dictionary<string, string> Forms { get; set; } = [];
}
