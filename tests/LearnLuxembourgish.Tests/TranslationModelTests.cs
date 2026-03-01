using LearnLuxembourgish.Shared.Models;

namespace LearnLuxembourgish.Tests;

public class TranslationModelTests
{
    [Fact]
    public void TranslationRequest_RequiresTextAndLanguage()
    {
        var request = new TranslationRequest { Text = "Hello", SourceLanguage = "EN" };
        Assert.Equal("Hello", request.Text);
        Assert.Equal("EN", request.SourceLanguage);
    }

    [Fact]
    public void TranslationResult_HasRequiredProperties()
    {
        var result = new TranslationResult
        {
            OriginalText = "Hello",
            SourceLanguage = "EN",
            TranslatedText = "Moien",
            Provider = "DeepL"
        };

        Assert.Equal("Hello", result.OriginalText);
        Assert.Equal("Moien", result.TranslatedText);
        Assert.Equal("DeepL", result.Provider);
        Assert.Null(result.AudioUrl);
        Assert.Null(result.GrammarExplanation);
    }

    [Fact]
    public void FlashCardDto_HasCorrectDefaults()
    {
        var dto = new FlashCardDto
        {
            DeckName = "Basics"
        };

        Assert.Equal("Basics", dto.DeckName);
        Assert.Equal(0, dto.ReviewCount);
        Assert.Null(dto.LastReviewedAt);
    }

    [Theory]
    [InlineData("EN")]
    [InlineData("PL")]
    public void TranslationRequest_SupportsBothLanguages(string language)
    {
        var request = new TranslationRequest { Text = "Test", SourceLanguage = language };
        Assert.Equal(language, request.SourceLanguage);
    }
}
