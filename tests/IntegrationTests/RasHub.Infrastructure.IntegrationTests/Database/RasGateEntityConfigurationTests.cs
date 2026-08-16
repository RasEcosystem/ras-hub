using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Security;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasGateEntityConfigurationTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_536)]
    public async Task Database_rejects_ports_outside_the_valid_range(int port)
    {
        await using var db = _database.CreateContext();
        var rasGate = RasGateTestData.Create(port: port);
        db.RasGates.Add(rasGate);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("secret", rasGate.ApiKey);
    }

    [Fact]
    public async Task Save_remote_identity_changes_increments_configuration_revision_once()
    {
        await using var db = _database.CreateContext();
        var rasGate = RasGateTestData.Create();
        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        rasGate.Name = "Renamed";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, rasGate.ConfigurationRevision);

        rasGate.Url = "https://replacement.example.test";
        rasGate.Port = 8443;
        rasGate.ApiKey = "replacement-secret";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rasGate.ConfigurationRevision);
        Assert.Equal("replacement-secret", rasGate.ApiKey);
        var storedApiKey = await ReadStoredApiKeyAsync(db, rasGate.Id);
        Assert.NotEqual(rasGate.ApiKey, storedApiKey);
        Assert.Equal(
            rasGate.ApiKey,
            _database.ApiKeyProtector.Unprotect(storedApiKey));
    }

    [Fact]
    public async Task Save_api_key_encrypts_storage_and_materializes_plaintext()
    {
        await using var db = _database.CreateContext();
        var rasGate = RasGateTestData.Create(apiKey: "top-secret");
        db.RasGates.Add(rasGate);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("top-secret", rasGate.ApiKey);
        var storedApiKey = await ReadStoredApiKeyAsync(db, rasGate.Id);
        Assert.NotEqual(rasGate.ApiKey, storedApiKey);
        Assert.True(_database.ApiKeyProtector.IsProtected(storedApiKey));

        db.ChangeTracker.Clear();
        var reloaded = await db.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal("top-secret", reloaded.ApiKey);
        Assert.Equal(EntityState.Unchanged, db.Entry(reloaded).State);
    }

    [Fact]
    public async Task Protect_legacy_key_rewrites_plaintext_once()
    {
        await using var db = _database.CreateContext();
        const string legacyApiKey = "legacy-secret";
        var rasGate = RasGateTestData.Create(apiKey: legacyApiKey);
        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ras_gates SET api_key = {legacyApiKey} WHERE id = {rasGate.Id}",
            TestContext.Current.CancellationToken);

        var migrator = new RasGateApiKeyProtectionMigrator(
            db,
            _database.ApiKeyProtector,
            NullLogger<RasGateApiKeyProtectionMigrator>.Instance);
        await migrator.ProtectLegacyKeysAsync(
            TestContext.Current.CancellationToken);
        var protectedApiKey = await ReadStoredApiKeyAsync(db, rasGate.Id);

        Assert.NotEqual(legacyApiKey, protectedApiKey);
        Assert.True(_database.ApiKeyProtector.IsProtected(protectedApiKey));

        await migrator.ProtectLegacyKeysAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            protectedApiKey,
            await ReadStoredApiKeyAsync(db, rasGate.Id));

        db.ChangeTracker.Clear();
        var reloaded = await db.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(legacyApiKey, reloaded.ApiKey);
    }

    private static Task<string> ReadStoredApiKeyAsync(
        RasHubDbContext db,
        Guid rasGateId)
    {
        return db.Database
            .SqlQuery<string>(
                $"SELECT api_key AS \"Value\" FROM ras_gates WHERE id = {rasGateId}")
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}