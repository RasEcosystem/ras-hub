using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RasHub.Infrastructure.Database.Security;

public sealed class RasGateApiKeyProtectionMigrator(
    RasHubDbContext db,
    RasGateApiKeyProtector protector,
    ILogger<RasGateApiKeyProtectionMigrator> logger)
{
    public async Task ProtectLegacyKeysAsync(
        CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken);
            var storedKeys = await db.Database
                .SqlQueryRaw<string>(
                    "SELECT api_key AS \"Value\" FROM ras_gates")
                .Distinct()
                .ToListAsync(cancellationToken);
            var migratedCount = 0;

            foreach (var storedKey in storedKeys)
            {
                if (protector.IsProtected(storedKey))
                {
                    _ = protector.Unprotect(storedKey);
                    continue;
                }

                var protectedKey = protector.Protect(storedKey);
                migratedCount += await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE ras_gates SET api_key = {protectedKey} WHERE api_key = {storedKey}",
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            if (migratedCount > 0)
                logger.LogInformation(
                    "Protected {RasGateCount} legacy RasGate API keys at rest",
                    migratedCount);
        });
    }
}
