using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RasHub.Domain;

namespace RasHub.Infrastructure.Database.EntityTypeConfigurations;

public sealed class RasInfobaseEntityTypeConfiguration
    : IEntityTypeConfiguration<RasInfobase>
{
    public void Configure(EntityTypeBuilder<RasInfobase> builder)
    {
        builder.ToTable("ras_infobases");

        builder.ConfigureCommonFields();

        builder.Property(infobase => infobase.RasClusterId)
            .HasColumnName("ras_cluster_id")
            .IsRequired();

        builder.Property(infobase => infobase.ExternalId)
            .HasColumnName("external_id")
            .IsRequired();

        builder.Property(infobase => infobase.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(infobase => infobase.Description)
            .HasColumnName("description")
            .IsRequired();

        builder.Property(infobase => infobase.ObservedAt)
            .HasColumnName("observed_at")
            .IsRequired();

        builder.HasIndex(infobase => new { infobase.RasClusterId, infobase.ExternalId })
            .IsUnique()
            .HasDatabaseName(
                "ux_ras_infobases_ras_cluster_id_external_id");

        builder.HasOne<RasCluster>()
            .WithMany()
            .HasForeignKey(infobase => infobase.RasClusterId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_ras_infobases_ras_clusters_ras_cluster_id");
    }
}