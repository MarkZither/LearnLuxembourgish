using System.Text.Json;
using System.Text.RegularExpressions;
using LearnLuxembourgish.Api.Services;
using LearnLuxembourgish.Data.Shared;
using LearnLuxembourgish.Data.Shared.Entities;
using LearnLuxembourgish.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LearnLuxembourgish.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TranslationsController : ControllerBase
{
    private readonly ITranslationService _translationService;
    private readonly IGrammarService _grammarService;
    private readonly LearnLuxembourgishDbContext _dbContext;
    private readonly ILogger<TranslationsController> _logger;

    public TranslationsController(
        ITranslationService translationService,
        IGrammarService grammarService,
        LearnLuxembourgishDbContext dbContext,
        ILogger<TranslationsController> logger)
    {
        _translationService = translationService;
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

        // Check audio cache — if found, include URL; otherwise ask client to generate it
        var textHash = AudioController.ComputeTextHash(result.TranslatedText);
        var cachedAudio = await _dbContext.TranslationAudios
            .FirstOrDefaultAsync(a => a.TextHash == textHash, cancellationToken);

        if (cachedAudio is not null)
        {
            result.AudioUrl = $"api/audio/{cachedAudio.Id}";
            result.AudioRequired = false;
            _logger.LogInformation("Audio cache hit for translation, audio id {AudioId}", cachedAudio.Id);
        }
        else
        {
            result.AudioRequired = true;
            _logger.LogInformation("No cached audio — client will generate and upload");
        }

        // Grammar explanation
        _logger.LogInformation("Starting grammar generation");
        var grammarTask = _grammarService.ExplainGrammarAsync(
            request.Text,
            result.TranslatedText,
            apiKey: request.GrammarApiKey,
            provider: request.GrammarProvider,
            grammarAspects: request.GrammarAspects,
            cancellationToken: cancellationToken);

        result.GrammarExplanation = await grammarTask;
        _logger.LogInformation("Grammar generation completed. Audio cached: {HasAudio}, Grammar: {HasGrammar}",
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
        var translations = await _dbContext.Translations
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.SourceText,
                t.SourceLanguage,
                t.TranslatedText,
                t.AudioUrl,
                t.GrammarExplanation,
                t.TranslationProvider
            })
            .ToListAsync(cancellationToken);

        // For translations without a cached audio URL, check the audio cache now
        var textsNeedingAudio = translations
            .Where(t => t.AudioUrl is null)
            .Select(t => AudioController.ComputeTextHash(t.TranslatedText))
            .Distinct()
            .ToList();

        Dictionary<string, string> hashToUrl = [];
        if (textsNeedingAudio.Count > 0)
        {
            var cachedAudios = await _dbContext.TranslationAudios
                .Where(a => textsNeedingAudio.Contains(a.TextHash))
                .Select(a => new { a.TextHash, a.Id })
                .ToListAsync(cancellationToken);

            hashToUrl = cachedAudios.ToDictionary(a => a.TextHash, a => $"api/audio/{a.Id}");
        }

        var results = translations.Select(t =>
        {
            var audioUrl = t.AudioUrl;
            if (audioUrl is null)
            {
                var hash = AudioController.ComputeTextHash(t.TranslatedText);
                hashToUrl.TryGetValue(hash, out audioUrl);
            }

            return new TranslationResult
            {
                TranslationId = t.Id,
                OriginalText = t.SourceText,
                SourceLanguage = t.SourceLanguage,
                TranslatedText = t.TranslatedText,
                AudioUrl = audioUrl,
                AudioRequired = audioUrl is null,
                GrammarExplanation = t.GrammarExplanation,
                Provider = t.TranslationProvider ?? "Unknown"
            };
        });

        return Ok(results);
    }
}
