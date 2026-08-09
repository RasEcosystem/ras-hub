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
        var rasGate = RasGateTestData.Create();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        await using (var db = _database.CreateContext())
        {
            db.RasGates.Add(rasGate);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var store = new RasClusterSnapshotStore(db);
            await store.ApplyAsync(
                rasGate.Id,
                [CreateSnapshot(firstId, "First"), CreateSnapshot(secondId, "Second")],
                DateTime.UtcNow.AddMinutes(-2),
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var store = new RasClusterSnapshotStore(db);
            await store.ApplyAsync(
                rasGate.Id,
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
                rasGate.Id,
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