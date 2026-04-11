using LearnLuxembourgish.Shared.Models;

namespace LearnLuxembourgish.Api.Services;

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}

public interface IGrammarService
{
    Task<string?> ExplainGrammarAsync(
        string sourceText,
        string translatedText,
        string? apiKey = null,
        string? provider = null,
        IEnumerable<string>? grammarAspects = null,
        CancellationToken cancellationToken = default);

    Task<List<VerbConjugationTable>?> ConjugateVerbsAsync(
        IEnumerable<string> infinitives,
        string? apiKey = null,
        string? provider = null,
        CancellationToken cancellationToken = default);
}
