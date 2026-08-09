using Microsoft.EntityFrameworkCore;
using RasHub.Infrastructure.Extensions;
using RasHub.Web.Data;

namespace RasHub.Web.Infrastructure.Database;

internal static class ApplicationDatabaseStartupExtensions
{
    public static async Task<bool> ApplyDatabaseStartupPolicyAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        var migrateOnly = string.Equals(
            app.Configuration["mode"],
            "migrate",
            StringComparison.OrdinalIgnoreCase);
        var applyMigrations = app.Configuration.GetValue<bool>(
            "Database:ApplyMigrations");

        if (migrateOnly || applyMigrations)
        {
            await app.Services.MigrateRasHubDatabaseAsync(cancellationToken);
            await app.Services.MigrateIdentityDatabaseAsync(cancellationToken);
        }
        else
        {
            await app.Services.EnsureNonNpgsqlIdentityDatabaseAsync(
                cancellationToken);
        }

        return !migrateOnly;
    }

    private static async Task MigrateIdentityDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.MigrateAsync(cancellationToken);
        else
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    private static async Task EnsureNonNpgsqlIdentityDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        if (!dbContext.Database.IsNpgsql())
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}