namespace LearnLuxembourgish.Data.Shared.Entities;

public class FlashCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid TranslationId { get; set; }
    public Translation? Translation { get; set; }
    public required string DeckName { get; set; }
    public required string UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastReviewedAt { get; set; }
    public int ReviewCount { get; set; }
}
