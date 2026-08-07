using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RasHub.Infrastructure.Database.EntityTypeConfigurations;

internal sealed class SettingEntryEntityTypeConfiguration
    : IEntityTypeConfiguration<SettingEntry>
{
    public void Configure(EntityTypeBuilder<SettingEntry> builder)
    {
        builder.ToTable("settings");

        builder.HasKey(entry => entry.Key)
            .HasName("pk_settings");

        builder.Property(entry => entry.Key)
            .HasColumnName("key")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(entry => entry.Value)
            .HasColumnName("value")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(entry => entry.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}