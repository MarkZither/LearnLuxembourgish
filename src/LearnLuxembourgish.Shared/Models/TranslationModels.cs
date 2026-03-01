namespace LearnLuxembourgish.Shared.Models;

public class TranslationRequest
{
    public required string Text { get; set; }
    public required string SourceLanguage { get; set; }
}

public class TranslationResult
{
    public required string OriginalText { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TranslatedText { get; set; }
    public string? AudioUrl { get; set; }
    public string? GrammarExplanation { get; set; }
    public required string Provider { get; set; }
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
