namespace LearnLuxembourgish.Data.Shared.Entities;

public class Translation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string SourceText { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TranslatedText { get; set; }
    public string? AudioUrl { get; set; }
    public string? GrammarExplanation { get; set; }
    public string? TranslationProvider { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? UserId { get; set; }

    public ICollection<FlashCard> FlashCards { get; set; } = new List<FlashCard>();
}
