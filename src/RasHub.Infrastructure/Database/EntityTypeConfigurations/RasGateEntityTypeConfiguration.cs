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
    }
}