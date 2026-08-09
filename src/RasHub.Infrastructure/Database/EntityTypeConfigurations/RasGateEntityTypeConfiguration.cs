using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database.EntityTypeConfigurations;

public sealed class RasGateEntityTypeConfiguration : IEntityTypeConfiguration<RasGate>
{
    public void Configure(EntityTypeBuilder<RasGate> builder)
    {
        builder.ToTable("ras_gates", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_ras_gates_port",
                "port BETWEEN 1 AND 65535");
        });

        builder.ConfigureCommonFields();

        builder.Property(rasGate => rasGate.Name)
            .HasColumnName("name")
            .HasMaxLength(RasGate.NameMaxLength)
            .IsRequired();

        builder.Property(rasGate => rasGate.Url)
            .HasColumnName("url")
            .HasMaxLength(RasGate.UrlMaxLength)
            .IsRequired();

        builder.Property(rasGate => rasGate.Port)
            .HasColumnName("port")
            .IsRequired();

        builder.Property(rasGate => rasGate.ApiKey)
            .HasColumnName("api_key")
            .HasMaxLength(RasGate.ApiKeyMaxLength)
            .IsRequired();

        builder.Property(rasGate => rasGate.ConfigurationRevision)
            .HasColumnName("configuration_revision")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(rasGate => rasGate.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(rasGate => rasGate.IsDeleted)
            .IsConcurrencyToken();

        builder.Property(rasGate => rasGate.InstanceName)
            .HasColumnName("instance_name");

        builder.Property(rasGate => rasGate.Version)
            .HasColumnName("version");

        builder.Property(rasGate => rasGate.StatusObservedAt)
            .HasColumnName("status_observed_at");

        builder.Property(rasGate => rasGate.LastSeenAt)
            .HasColumnName("last_seen_at");
    }
}