using LearnLuxembourgish.Data.Shared;
using LearnLuxembourgish.Data.Shared.Entities;
using LearnLuxembourgish.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LearnLuxembourgish.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FlashCardsController : ControllerBase
{
    private readonly LearnLuxembourgishDbContext _dbContext;
    private readonly ILogger<FlashCardsController> _logger;

    public FlashCardsController(LearnLuxembourgishDbContext dbContext, ILogger<FlashCardsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FlashCardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<FlashCardDto>>> GetFlashCards(
        [FromQuery] string? deck = null,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var query = _dbContext.FlashCards
            .Include(f => f.Translation)
            .Where(f => f.UserId == userId);

        if (!string.IsNullOrEmpty(deck))
        {
            query = query.Where(f => f.DeckName == deck);
        }

        var cards = await query
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FlashCardDto
            {
                Id = f.Id,
                TranslationId = f.TranslationId,
                SourceText = f.Translation != null ? f.Translation.SourceText : null,
                TranslatedText = f.Translation != null ? f.Translation.TranslatedText : null,
                AudioUrl = f.Translation != null ? f.Translation.AudioUrl : null,
                GrammarExplanation = f.Translation != null ? f.Translation.GrammarExplanation : null,
                DeckName = f.DeckName,
                CreatedAt = f.CreatedAt,
                LastReviewedAt = f.LastReviewedAt,
                ReviewCount = f.ReviewCount
            })
            .ToListAsync(cancellationToken);

        return Ok(cards);
    }

    [HttpPost]
    [ProducesResponseType(typeof(FlashCardDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlashCardDto>> CreateFlashCard(
        [FromBody] FlashCardRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var translation = await _dbContext.Translations.FindAsync([request.TranslationId], cancellationToken);
        if (translation is null)
        {
            return NotFound($"Translation {request.TranslationId} not found.");
        }

        var flashCard = new FlashCard
        {
            TranslationId = request.TranslationId,
            DeckName = request.DeckName,
            UserId = userId
        };
        _dbContext.FlashCards.Add(flashCard);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetFlashCards), new { }, new FlashCardDto
        {
            Id = flashCard.Id,
            TranslationId = flashCard.TranslationId,
            SourceText = translation.SourceText,
            TranslatedText = translation.TranslatedText,
            AudioUrl = translation.AudioUrl,
            GrammarExplanation = translation.GrammarExplanation,
            DeckName = flashCard.DeckName,
            CreatedAt = flashCard.CreatedAt
        });
    }

    [HttpPatch("{id:guid}/review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordReview(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var card = await _dbContext.FlashCards.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, cancellationToken);
        if (card is null) return NotFound();

        card.ReviewCount++;
        card.LastReviewedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFlashCard(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var card = await _dbContext.FlashCards.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, cancellationToken);
        if (card is null) return NotFound();

        _dbContext.FlashCards.Remove(card);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
