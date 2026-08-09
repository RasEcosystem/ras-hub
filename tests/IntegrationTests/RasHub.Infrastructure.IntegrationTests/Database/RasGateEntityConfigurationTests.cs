using Microsoft.EntityFrameworkCore;

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
        db.RasGates.Add(RasGateTestData.Create(port: port));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));
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
    }
}