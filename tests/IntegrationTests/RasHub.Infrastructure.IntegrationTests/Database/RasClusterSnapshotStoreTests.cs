using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Models;
using RasHub.Domain.Enums;
using RasHub.Infrastructure.Database;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasClusterSnapshotStoreTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Apply_adds_updates_soft_deletes_and_restores_clusters()
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        await using (var db = _database.CreateContext())
        {
            db.AddRange(gate, endpoint);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var store = new RasClusterSnapshotStore(db);
            await store.ApplyAsync(
                endpoint.Id,
                [CreateSnapshot(firstId, "First"), CreateSnapshot(secondId, "Second")],
                DateTime.UtcNow.AddMinutes(-2),
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var store = new RasClusterSnapshotStore(db);
            await store.ApplyAsync(
                endpoint.Id,
                [CreateSnapshot(firstId, "First updated"), CreateSnapshot(thirdId, "Third")],
                DateTime.UtcNow.AddMinutes(-1),
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var active = await db.RasClusters
                .OrderBy(cluster => cluster.Name)
                .ToListAsync(TestContext.Current.CancellationToken);
            var deleted = await db.RasClusters
                .IgnoreQueryFilters()
                .SingleAsync(
                    cluster => cluster.ExternalId == secondId,
                    TestContext.Current.CancellationToken);

            Assert.Equal(["First updated", "Third"], active.Select(cluster => cluster.Name));
            Assert.True(deleted.IsDeleted);
            Assert.NotNull(deleted.DeletedAt);
        }

        await using (var db = _database.CreateContext())
        {
            var store = new RasClusterSnapshotStore(db);
            await store.ApplyAsync(
                endpoint.Id,
                [CreateSnapshot(secondId, "Second restored")],
                DateTime.UtcNow,
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var restored = await db.RasClusters.SingleAsync(
                cluster => cluster.ExternalId == secondId,
                TestContext.Current.CancellationToken);
            var all = await db.RasClusters
                .IgnoreQueryFilters()
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal("Second restored", restored.Name);
            Assert.False(restored.IsDeleted);
            Assert.Null(restored.DeletedAt);
            Assert.Equal(3, all.Count);
            Assert.Equal(2, all.Count(cluster => cluster.IsDeleted));
        }
    }

    [Fact]
    public async Task Upsert_updates_one_cluster_without_deleting_other_clusters()
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await using (var db = _database.CreateContext())
        {
            db.AddRange(gate, endpoint);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var store = new RasClusterSnapshotStore(db);
            await store.ApplyAsync(
                endpoint.Id,
                [CreateSnapshot(firstId, "First"), CreateSnapshot(secondId, "Second")],
                DateTime.UtcNow.AddMinutes(-1),
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var store = new RasClusterSnapshotStore(db);
            await store.UpsertAsync(
                endpoint.Id,
                CreateSnapshot(firstId, "First updated"),
                DateTime.UtcNow,
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var clusters = await db.RasClusters
                .OrderBy(cluster => cluster.Name)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, clusters.Count);
            Assert.Contains(clusters,
                cluster =>
                    cluster.ExternalId == firstId &&
                    cluster.Name == "First updated");
            Assert.Contains(clusters,
                cluster =>
                    cluster.ExternalId == secondId &&
                    cluster.Name == "Second");
        }
    }

    [Fact]
    public async Task Remove_soft_deletes_requested_cluster_only()
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        var first = RasClusterTestData.Create(endpoint.Id, name: "First");
        var second = RasClusterTestData.Create(endpoint.Id, name: "Second");

        await using (var db = _database.CreateContext())
        {
            db.AddRange(gate, endpoint, first, second);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var store = new RasClusterSnapshotStore(db);
            await store.RemoveAsync(
                endpoint.Id,
                first.ExternalId,
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var active = await db.RasClusters
                .SingleAsync(TestContext.Current.CancellationToken);
            var removed = await db.RasClusters
                .IgnoreQueryFilters()
                .SingleAsync(
                    cluster => cluster.ExternalId == first.ExternalId,
                    TestContext.Current.CancellationToken);

            Assert.Equal(second.ExternalId, active.ExternalId);
            Assert.True(removed.IsDeleted);
            Assert.NotNull(removed.DeletedAt);
        }
    }

    [Fact]
    public async Task Remove_cluster_soft_deletes_its_infobases()
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var infobase = RasInfobaseTestData.Create(cluster.Id);

        await using (var db = _database.CreateContext())
        {
            db.AddRange(gate, endpoint, cluster, infobase);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var store = new RasClusterSnapshotStore(db);
            await store.RemoveAsync(
                endpoint.Id,
                cluster.ExternalId,
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var removedInfobase = await db.RasInfobases
                .IgnoreQueryFilters()
                .SingleAsync(TestContext.Current.CancellationToken);

            Assert.True(removedInfobase.IsDeleted);
            Assert.NotNull(removedInfobase.DeletedAt);
        }
    }

    private static RasClusterSnapshot CreateSnapshot(Guid externalId, string name)
    {
        return new RasClusterSnapshot
        {
            ExternalId = externalId,
            Name = name,
            Host = "localhost",
            Port = 1541,
            ExpirationTimeoutSeconds = 60,
            LifetimeLimitSeconds = 0,
            MaxMemorySizeKb = 0,
            MaxMemoryTimeLimitSeconds = 0,
            SecurityLevel = 0,
            SessionFaultToleranceLevel = 0,
            LoadBalancingMode = RasClusterLoadBalancingMode.Performance,
            ErrorsCountThresholdPercent = 0,
            KillProblemProcesses = false
        };
    }
}
