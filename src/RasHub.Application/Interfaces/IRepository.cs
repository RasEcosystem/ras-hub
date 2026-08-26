using System.Linq.Expressions;
using RasHub.Domain.Abstractions;

namespace RasHub.Application.Interfaces;

public interface IRepository<T> where T : class, IEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<List<T>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    Task<List<T>> ListAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken);

    Task AddAsync(T entity, CancellationToken cancellationToken);

    void Remove(T entity);
}
