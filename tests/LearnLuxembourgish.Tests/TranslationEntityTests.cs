using LearnLuxembourgish.Data.Shared;
using LearnLuxembourgish.Data.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnLuxembourgish.Tests;

public class TranslationEntityTests
{
    private static LearnLuxembourgishDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LearnLuxembourgishDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LearnLuxembourgishDbContext(options);
    }

    [Fact]
    public async Task CanAddAndRetrieveTranslation()
    {
        using var context = CreateContext();

        var translation = new Translation
        {
            SourceText = "Good morning",
            SourceLanguage = "EN",
            TranslatedText = "Gudde Moien",
            TranslationProvider = "Test"
        };

        context.Translations.Add(translation);
        await context.SaveChangesAsync();

        var retrieved = await context.Translations.FindAsync(translation.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Good morning", retrieved.SourceText);
        Assert.Equal("Gudde Moien", retrieved.TranslatedText);
    }

    [Fact]
    public async Task CanAddFlashCardLinkedToTranslation()
    {
        using var context = CreateContext();

        var translation = new Translation
        {
            SourceText = "Thank you",
            SourceLanguage = "EN",
            TranslatedText = "Merci",
            TranslationProvider = "Test",
            UserId = "user123"
        };
        context.Translations.Add(translation);
        await context.SaveChangesAsync();

        var flashCard = new FlashCard
        {
            TranslationId = translation.Id,
            DeckName = "Basics",
            UserId = "user123"
        };
        context.FlashCards.Add(flashCard);
        await context.SaveChangesAsync();

        var retrieved = await context.FlashCards
            .Include(f => f.Translation)
            .FirstAsync(f => f.Id == flashCard.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("Basics", retrieved.DeckName);
        Assert.Equal("Thank you", retrieved.Translation!.SourceText);
    }

    [Fact]
    public async Task DeletingTranslationCascadesToFlashCards()
    {
        using var context = CreateContext();

        var translation = new Translation
        {
            SourceText = "Goodbye",
            SourceLanguage = "EN",
            TranslatedText = "Äddi",
            TranslationProvider = "Test",
            UserId = "user123"
        };
        context.Translations.Add(translation);
        await context.SaveChangesAsync();

        context.FlashCards.Add(new FlashCard
        {
            TranslationId = translation.Id,
            DeckName = "Basics",
            UserId = "user123"
        });
        await context.SaveChangesAsync();

        context.Translations.Remove(translation);
        await context.SaveChangesAsync();

        var cardCount = await context.FlashCards.CountAsync();
        Assert.Equal(0, cardCount);
    }
}
