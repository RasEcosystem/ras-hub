using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Services;
using RasHub.Domain;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.RasGates.Endpoints;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasGateRegistryTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Unregister_and_restore_preserve_endpoint_shadow_and_advance_revision()
    {
        var observedAt = new DateTime(
            2026,
            8,
            20,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var rasGate = RasGateTestData.Create();
        rasGate.InstanceName = "Remote Gate";
        rasGate.Version = "1.2.3";
        rasGate.StatusObservedAt = observedAt;
        rasGate.RacAvailable = true;
        rasGate.RacVersion = "8.3.27.2214";
        rasGate.RacStatusObservedAt = observedAt;
        rasGate.LastSeenAt = observedAt;
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var infobase = RasInfobaseTestData.Create(cluster.Id);

        await using var db = _database.CreateContext();
        db.AddRange(endpoint, rasGate, cluster, infobase);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = new RasGateRegistry(
            new EfRepository<RasGate>(db),
            new RasGateEndpointFactory(),
            db);

        var removed = await registry.UnregisterAsync(
            rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(removed);
        Assert.True(removed.IsDeleted);
        Assert.Equal(2, removed.ConfigurationRevision);
        AssertRemoteStateIsInvalidated(removed);
        Assert.False((await db.RasClusters.SingleAsync(
            TestContext.Current.CancellationToken)).IsDeleted);
        Assert.False((await db.RasInfobases.SingleAsync(
            TestContext.Current.CancellationToken)).IsDeleted);

        await registry.RestoreAsync(
            removed,
            TestContext.Current.CancellationToken);

        Assert.False(removed.IsDeleted);
        Assert.Null(removed.DeletedAt);
        Assert.Equal(3, removed.ConfigurationRevision);
        AssertRemoteStateIsInvalidated(removed);
        Assert.Single(await db.RasClusters.ToListAsync(
            TestContext.Current.CancellationToken));
        Assert.Single(await db.RasInfobases.ToListAsync(
            TestContext.Current.CancellationToken));
    }

    private static void AssertRemoteStateIsInvalidated(RasGate rasGate)
    {
        Assert.Null(rasGate.InstanceName);
        Assert.Null(rasGate.Version);
        Assert.Null(rasGate.StatusObservedAt);
        Assert.Null(rasGate.RacAvailable);
        Assert.Null(rasGate.RacVersion);
        Assert.Null(rasGate.RacStatusObservedAt);
        Assert.Null(rasGate.LastSeenAt);
    }
}
