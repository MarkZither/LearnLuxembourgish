using System.Text.Json;
using System.Text.RegularExpressions;
using LearnLuxembourgish.Api.Services;
using LearnLuxembourgish.Data.Shared;
using LearnLuxembourgish.Data.Shared.Entities;
using LearnLuxembourgish.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LearnLuxembourgish.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TranslationsController : ControllerBase
{
    private readonly ITranslationService _translationService;
    private readonly IAudioService _audioService;
    private readonly IGrammarService _grammarService;
    private readonly LearnLuxembourgishDbContext _dbContext;
    private readonly ILogger<TranslationsController> _logger;

    public TranslationsController(
        ITranslationService translationService,
        IAudioService audioService,
        IGrammarService grammarService,
        LearnLuxembourgishDbContext dbContext,
        ILogger<TranslationsController> logger)
    {
        _translationService = translationService;
        _audioService = audioService;
        _grammarService = grammarService;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(TranslationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TranslationResult>> Translate(
        [FromBody] TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest("Text is required.");
        }

        if (request.SourceLanguage != "EN" && request.SourceLanguage != "PL")
        {
            return BadRequest("SourceLanguage must be 'EN' or 'PL'.");
        }

        _logger.LogInformation("Translation request received for {SourceLanguage} text", request.SourceLanguage);

        var result = await _translationService.TranslateAsync(request, cancellationToken);
        _logger.LogInformation("Core translation completed with provider {Provider}", result.Provider);

        // Generate audio and grammar explanation in parallel
        _logger.LogInformation("Starting parallel audio and grammar generation");
        var audioTask = _audioService.GenerateAudioAsync(result.TranslatedText, cancellationToken);
        var grammarTask = _grammarService.ExplainGrammarAsync(
            request.Text,
            result.TranslatedText,
            apiKey: request.GrammarApiKey,
            provider: request.GrammarProvider,
            grammarAspects: request.GrammarAspects,
            cancellationToken: cancellationToken);
        await Task.WhenAll(audioTask, grammarTask);

        var (audioUrl, audioError) = await audioTask;
        result.AudioUrl = audioUrl;
        result.AudioError = audioError;
        result.GrammarExplanation = await grammarTask;
        _logger.LogInformation("Audio and grammar generation completed. Audio: {HasAudio}, Grammar: {HasGrammar}",
            result.AudioUrl != null, result.GrammarExplanation != null);

        if (result.GrammarExplanation is not null)
        {
            var infinitives = ExtractVerbInfinitives(result.GrammarExplanation);
            result.RevisedTranslation = ExtractRevisedTranslation(result.GrammarExplanation);

            // Strip machine-readable blocks from the markdown before rendering
            result.GrammarExplanation = Regex.Replace(
                result.GrammarExplanation,
                @"\n?```verbs-json\s*[\s\S]*?```\n?",
                string.Empty,
                RegexOptions.Singleline);
            result.GrammarExplanation = Regex.Replace(
                result.GrammarExplanation,
                @"\n?```revised-translation\s*[\s\S]*?```\n?",
                string.Empty,
                RegexOptions.Singleline).Trim();
            if (infinitives is { Count: > 0 })
            {
                _logger.LogInformation("Fetching conjugation tables for {Count} verbs", infinitives.Count);
                try
                {
                    result.VerbConjugations = await _grammarService.ConjugateVerbsAsync(
                        infinitives,
                        apiKey: request.GrammarApiKey,
                        provider: request.GrammarProvider,
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Verb conjugation call failed, continuing without conjugations");
                }
            }
        }

        // Persist if user is authenticated
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var translation = new Translation
            {
                SourceText = request.Text,
                SourceLanguage = request.SourceLanguage,
                TranslatedText = result.TranslatedText,
                AudioUrl = result.AudioUrl,
                GrammarExplanation = result.GrammarExplanation,
                TranslationProvider = result.Provider,
                UserId = userId
            };
            _dbContext.Translations.Add(translation);
            await _dbContext.SaveChangesAsync(cancellationToken);
            result.TranslationId = translation.Id;
            _logger.LogInformation("Translation saved to database with ID {TranslationId} for user {UserId}", translation.Id, userId);
        }

        _logger.LogInformation("Translation request completed successfully");
        return Ok(result);
    }

    private static string? ExtractRevisedTranslation(string grammarExplanation)
    {
        var match = Regex.Match(grammarExplanation, @"```revised-translation\s*([\s\S]*?)\s*```", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static List<string>? ExtractVerbInfinitives(string grammarExplanation)
    {
        var match = Regex.Match(grammarExplanation, @"```verbs-json\s*(\[[\s\S]*?\])\s*```", RegexOptions.Singleline);
        if (!match.Success) return null;
        try
        {
            var verbs = JsonSerializer.Deserialize<List<VerbFormInSentence>>(
                match.Groups[1].Value.Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return verbs?.Select(v => v.Infinitive).Distinct().ToList();
        }
        catch { return null; }
    }

    private record VerbFormInSentence(string Infinitive, string FormUsed, string English);

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<TranslationResult>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<TranslationResult>>> GetMyTranslations(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var translations = _dbContext.Translations
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TranslationResult
            {
                OriginalText = t.SourceText,
                SourceLanguage = t.SourceLanguage,
                TranslatedText = t.TranslatedText,
                AudioUrl = t.AudioUrl,
                GrammarExplanation = t.GrammarExplanation,
                Provider = t.TranslationProvider ?? "Unknown"
            });
        return Ok(translations);
    }
}
