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
    public async Task Unregister_and_restore_invalidate_derived_state_and_advance_revision()
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
        var cluster = RasClusterTestData.Create(rasGate.Id);
        var infobase = RasInfobaseTestData.Create(cluster.Id);

        await using var db = _database.CreateContext();
        db.AddRange(rasGate, cluster, infobase);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = new RasGateRegistry(
            new EfRepository<RasGate>(db),
            new RasClusterSnapshotStore(db),
            new RasGateEndpointFactory(),
            db);

        var removed = await registry.UnregisterAsync(
            rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(removed);
        Assert.True(removed.IsDeleted);
        Assert.Equal(2, removed.ConfigurationRevision);
        AssertRemoteStateIsInvalidated(removed);
        Assert.True((await db.RasClusters
            .IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken)).IsDeleted);
        Assert.True((await db.RasInfobases
            .IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken)).IsDeleted);

        await registry.RestoreAsync(
            removed,
            TestContext.Current.CancellationToken);

        Assert.False(removed.IsDeleted);
        Assert.Null(removed.DeletedAt);
        Assert.Equal(3, removed.ConfigurationRevision);
        AssertRemoteStateIsInvalidated(removed);
        Assert.Empty(await db.RasClusters.ToListAsync(
            TestContext.Current.CancellationToken));
        Assert.Empty(await db.RasInfobases.ToListAsync(
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
