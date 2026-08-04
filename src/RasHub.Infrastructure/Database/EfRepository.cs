using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Application.Interfaces;
using RasHub.Domain.Abstractions;

namespace RasHub.Infrastructure.Database;

public sealed class EfRepository<T>(RasHubDbContext dbContext) : IRepository<T>
    where T : class, IEntity
{
    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Set<T>()
            .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
    }

    public async Task<List<T>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        var entityIds = ids.Distinct().ToArray();

        return await dbContext.Set<T>()
            .Where(entity => entityIds.Contains(entity.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<T>> ListAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<T>()
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(T entity, CancellationToken cancellationToken)
    {
        await dbContext.Set<T>().AddAsync(entity, cancellationToken);
    }

    public void Remove(T entity)
    {
        dbContext.Set<T>().Remove(entity);
    }
}