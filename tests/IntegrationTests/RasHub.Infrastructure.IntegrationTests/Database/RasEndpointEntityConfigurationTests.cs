using Microsoft.EntityFrameworkCore;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasEndpointEntityConfigurationTests : IDisposable
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
        db.RasEndpoints.Add(RasEndpointTestData.Create(port: port));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_configuration_changes_increment_configuration_revision()
    {
        await using var db = _database.CreateContext();
        var endpoint = RasEndpointTestData.Create();
        db.RasEndpoints.Add(endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        endpoint.Name = "Renamed RAS";
        endpoint.Host = "replacement.example.test";
        endpoint.Port = 2545;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, endpoint.ConfigurationRevision);

        endpoint.IsActive = false;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, endpoint.ConfigurationRevision);
    }

    [Fact]
    public async Task Delete_and_restore_are_soft_and_increment_revision()
    {
        await using var db = _database.CreateContext();
        var endpoint = RasEndpointTestData.Create();
        db.RasEndpoints.Add(endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.RasEndpoints.Remove(endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(endpoint.IsDeleted);
        Assert.NotNull(endpoint.DeletedAt);
        Assert.Equal(2, endpoint.ConfigurationRevision);
        Assert.Empty(await db.RasEndpoints.ToListAsync(
            TestContext.Current.CancellationToken));

        endpoint.IsDeleted = false;
        endpoint.DeletedAt = null;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(endpoint.IsDeleted);
        Assert.Null(endpoint.DeletedAt);
        Assert.Equal(3, endpoint.ConfigurationRevision);
    }
}
