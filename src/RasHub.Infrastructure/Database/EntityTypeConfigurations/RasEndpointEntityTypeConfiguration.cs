using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database.EntityTypeConfigurations;

public sealed class RasEndpointEntityTypeConfiguration
    : IEntityTypeConfiguration<RasEndpoint>
{
    public void Configure(EntityTypeBuilder<RasEndpoint> builder)
    {
        builder.ToTable("ras_endpoints",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_ras_endpoints_port",
                    "port BETWEEN 1 AND 65535");
            });

        builder.ConfigureCommonFields();

        builder.Property(endpoint => endpoint.Name)
            .HasColumnName("name")
            .HasMaxLength(RasEndpoint.NameMaxLength)
            .IsRequired();

        builder.Property(endpoint => endpoint.Host)
            .HasColumnName("host")
            .HasMaxLength(RasEndpoint.HostMaxLength)
            .IsRequired();

        builder.Property(endpoint => endpoint.Port)
            .HasColumnName("port")
            .IsRequired();

        builder.Property(endpoint => endpoint.ConfigurationRevision)
            .HasColumnName("configuration_revision")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(endpoint => endpoint.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(endpoint => endpoint.IsDeleted)
            .IsConcurrencyToken();
    }
}
