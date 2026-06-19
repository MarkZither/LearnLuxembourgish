namespace LearnLuxembourgish.Data.Shared.Entities;

public class TranslationAudio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string LuxembourgishText { get; set; }
    public required string TextHash { get; set; }  // SHA-256 hex of normalized text — unique index key
    public required byte[] AudioData { get; set; }
    public required string ContentType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
