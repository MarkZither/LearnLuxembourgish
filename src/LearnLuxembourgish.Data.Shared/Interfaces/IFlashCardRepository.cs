using LearnLuxembourgish.Data.Shared.Entities;

namespace LearnLuxembourgish.Data.Shared.Interfaces;

public interface IFlashCardRepository
{
    Task<FlashCard?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<FlashCard>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<FlashCard>> GetByDeckAsync(string userId, string deckName, CancellationToken cancellationToken = default);
    Task<FlashCard> AddAsync(FlashCard flashCard, CancellationToken cancellationToken = default);
    Task UpdateAsync(FlashCard flashCard, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
