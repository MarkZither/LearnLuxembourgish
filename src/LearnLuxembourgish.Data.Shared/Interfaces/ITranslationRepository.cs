using LearnLuxembourgish.Data.Shared.Entities;

namespace LearnLuxembourgish.Data.Shared.Interfaces;

public interface ITranslationRepository
{
    Task<Translation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Translation>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<Translation> AddAsync(Translation translation, CancellationToken cancellationToken = default);
    Task UpdateAsync(Translation translation, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
