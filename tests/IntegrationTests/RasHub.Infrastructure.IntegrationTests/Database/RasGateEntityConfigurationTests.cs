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

    [Theory]
    [InlineData("endpoint")]
    [InlineData("api-key")]
    [InlineData("deactivate")]
    [InlineData("delete")]
    public async Task Save_remote_access_change_invalidates_status_observations(
        string change)
    {
        await using var db = _database.CreateContext();
        var rasGate = RasGateTestData.Create();
        rasGate.InstanceName = "Remote Gate";
        rasGate.Version = "1.2.3";
        rasGate.StatusObservedAt = DateTime.UtcNow;
        rasGate.RacAvailable = true;
        rasGate.RacVersion = "8.3.27.2214";
        rasGate.RacStatusObservedAt = DateTime.UtcNow;
        rasGate.LastSeenAt = DateTime.UtcNow;
        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        switch (change)
        {
            case "endpoint":
                rasGate.Url = "https://replacement.example.test";
                break;
            case "api-key":
                rasGate.ApiKey = "replacement-secret";
                break;
            case "deactivate":
                rasGate.IsActive = false;
                break;
            case "delete":
                db.RasGates.Remove(rasGate);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rasGate.ConfigurationRevision);
        Assert.Null(rasGate.InstanceName);
        Assert.Null(rasGate.Version);
        Assert.Null(rasGate.StatusObservedAt);
        Assert.Null(rasGate.RacAvailable);
        Assert.Null(rasGate.RacVersion);
        Assert.Null(rasGate.RacStatusObservedAt);
        Assert.Null(rasGate.LastSeenAt);
    }

    [Fact]
    public async Task Save_restore_advances_revision_without_restoring_observations()
    {
        await using var db = _database.CreateContext();
        var rasGate = RasGateTestData.Create();
        rasGate.InstanceName = "Remote Gate";
        rasGate.Version = "1.2.3";
        rasGate.StatusObservedAt = DateTime.UtcNow;
        rasGate.RacAvailable = true;
        rasGate.RacVersion = "8.3.27.2214";
        rasGate.RacStatusObservedAt = DateTime.UtcNow;
        rasGate.LastSeenAt = DateTime.UtcNow;
        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.RasGates.Remove(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        rasGate.IsDeleted = false;
        rasGate.DeletedAt = null;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, rasGate.ConfigurationRevision);
        Assert.Null(rasGate.InstanceName);
        Assert.Null(rasGate.Version);
        Assert.Null(rasGate.StatusObservedAt);
        Assert.Null(rasGate.RacAvailable);
        Assert.Null(rasGate.RacVersion);
        Assert.Null(rasGate.RacStatusObservedAt);
        Assert.Null(rasGate.LastSeenAt);
    }

    [Fact]
    public async Task Save_reactivation_advances_revision_without_restoring_observations()
    {
        await using var db = _database.CreateContext();
        var rasGate = RasGateTestData.Create();
        rasGate.IsActive = false;
        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        rasGate.IsActive = true;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rasGate.ConfigurationRevision);
        Assert.Null(rasGate.RacAvailable);
        Assert.Null(rasGate.RacVersion);
        Assert.Null(rasGate.RacStatusObservedAt);
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
    public async Task Save_plaintext_api_key_with_protection_prefix_encrypts_and_round_trips()
    {
        await using var db = _database.CreateContext();
        const string apiKey = "rashub-dp:v1:operator-selected-key";
        var rasGate = RasGateTestData.Create(apiKey: apiKey);
        db.RasGates.Add(rasGate);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var storedApiKey = await ReadStoredApiKeyAsync(db, rasGate.Id);
        Assert.NotEqual(apiKey, storedApiKey);
        Assert.True(_database.ApiKeyProtector.IsProtected(storedApiKey));
        Assert.Equal(apiKey, _database.ApiKeyProtector.Unprotect(storedApiKey));

        db.ChangeTracker.Clear();
        var reloaded = await db.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(apiKey, reloaded.ApiKey);
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
