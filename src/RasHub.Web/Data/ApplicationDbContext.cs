using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RasHub.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    protected override Version SchemaVersion => IdentitySchemaVersions.Version3;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(item => item.ApiKey)
                .HasMaxLength(ApplicationUser.ApiKeyMaxLength);
            user.HasIndex(item => item.ApiKey)
                .IsUnique()
                .HasFilter("\"ApiKey\" IS NOT NULL");
        });
    }
}