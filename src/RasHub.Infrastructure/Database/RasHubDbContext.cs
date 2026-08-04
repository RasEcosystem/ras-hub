using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RasHub.Application.Interfaces;
using RasHub.Domain.Abstractions;

namespace RasHub.Infrastructure.Database;

public sealed class RasHubDbContext(DbContextOptions<RasHubDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string ConnectionStringName = "RasHub";

    private const string SoftDeleteFilterName = "SoftDelete";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RasHubDbContext).Assembly);

        ApplySoftDeleteFilters(modelBuilder);
    }

    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        var softDeletableEntityTypes = modelBuilder.Model
            .GetEntityTypes()
            .Where(entityType =>
                entityType.BaseType is null &&
                typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType));

        foreach (var entityType in softDeletableEntityTypes)
        {
            var entity = Expression.Parameter(entityType.ClrType, "entity");
            var isDeleted = Expression.Property(entity, nameof(ISoftDeletable.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(isDeleted), entity);

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(SoftDeleteFilterName, filter);
        }
    }
}