using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RasHub.Domain.Abstractions;

namespace RasHub.Infrastructure.Database.EntityTypeConfigurations;

internal static class EntityTypeConfigurationExtensions
{
    private const string SoftDeleteFilterName = "SoftDelete";

    public static void ConfigureCommonFields<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IEntity, IAuditable, ISoftDeletable
    {
        var primaryKey = builder.HasKey(entity => entity.Id);
        var tableName = builder.Metadata.GetTableName();

        if (!string.IsNullOrWhiteSpace(tableName))
            primaryKey.HasName($"pk_{tableName}");

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(entity => entity.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(entity => entity.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(entity => entity.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasQueryFilter(
            SoftDeleteFilterName,
            entity => !entity.IsDeleted);
    }
}
