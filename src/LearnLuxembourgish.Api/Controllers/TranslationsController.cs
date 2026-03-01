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

        var result = await _translationService.TranslateAsync(request, cancellationToken);

        // Generate audio and grammar explanation in parallel
        var audioTask = _audioService.GenerateAudioAsync(result.TranslatedText, cancellationToken);
        var grammarTask = _grammarService.ExplainGrammarAsync(request.Text, result.TranslatedText, cancellationToken);
        await Task.WhenAll(audioTask, grammarTask);

        result.AudioUrl = await audioTask;
        result.GrammarExplanation = await grammarTask;

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
        }

        return Ok(result);
    }

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
