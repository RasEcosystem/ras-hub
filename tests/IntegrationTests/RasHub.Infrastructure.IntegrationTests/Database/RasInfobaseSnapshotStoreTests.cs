using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.Database;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasInfobaseSnapshotStoreTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Apply_adds_updates_soft_deletes_and_restores_infobases()
    {
        var rasGate = RasGateTestData.Create();
        var cluster = RasClusterTestData.Create(rasGate.Id);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        await using (var db = _database.CreateContext())
        {
            db.AddRange(rasGate, cluster);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var store = new RasInfobaseSnapshotStore(db);
            await store.ApplyAsync(
                cluster.Id,
                [
                    CreateSnapshot(firstId, "First"),
                    CreateSnapshot(secondId, "Second")
                ],
                DateTime.UtcNow.AddMinutes(-2),
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var store = new RasInfobaseSnapshotStore(db);
            await store.ApplyAsync(
                cluster.Id,
                [
                    CreateSnapshot(firstId, "First updated"),
                    CreateSnapshot(thirdId, "Third")
                ],
                DateTime.UtcNow.AddMinutes(-1),
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var active = await db.RasInfobases
                .OrderBy(infobase => infobase.Name)
                .ToListAsync(TestContext.Current.CancellationToken);
            var deleted = await db.RasInfobases
                .IgnoreQueryFilters()
                .SingleAsync(
                    infobase => infobase.ExternalId == secondId,
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                ["First updated", "Third"],
                active.Select(infobase => infobase.Name));
            Assert.True(deleted.IsDeleted);
            Assert.NotNull(deleted.DeletedAt);
        }

        await using (var db = _database.CreateContext())
        {
            var store = new RasInfobaseSnapshotStore(db);
            await store.ApplyAsync(
                cluster.Id,
                [CreateSnapshot(secondId, "Second restored")],
                DateTime.UtcNow,
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var restored = await db.RasInfobases.SingleAsync(
                infobase => infobase.ExternalId == secondId,
                TestContext.Current.CancellationToken);
            var all = await db.RasInfobases
                .IgnoreQueryFilters()
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal("Second restored", restored.Name);
            Assert.False(restored.IsDeleted);
            Assert.Null(restored.DeletedAt);
            Assert.Equal(3, all.Count);
            Assert.Equal(2, all.Count(infobase => infobase.IsDeleted));
        }
    }

    [Fact]
    public async Task Upsert_updates_one_infobase_without_deleting_siblings()
    {
        var rasGate = RasGateTestData.Create();
        var cluster = RasClusterTestData.Create(rasGate.Id);
        var first = RasInfobaseTestData.Create(cluster.Id, name: "First");
        var second = RasInfobaseTestData.Create(cluster.Id, name: "Second");

        await using (var db = _database.CreateContext())
        {
            db.AddRange(rasGate, cluster, first, second);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var store = new RasInfobaseSnapshotStore(db);
            await store.UpsertAsync(
                cluster.Id,
                CreateSnapshot(first.ExternalId, "First updated"),
                DateTime.UtcNow,
                TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = _database.CreateContext())
        {
            var infobases = await db.RasInfobases
                .OrderBy(infobase => infobase.Name)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, infobases.Count);
            Assert.Contains(infobases,
                infobase =>
                    infobase.ExternalId == first.ExternalId &&
                    infobase.Name == "First updated");
            Assert.Contains(infobases,
                infobase =>
                    infobase.ExternalId == second.ExternalId &&
                    infobase.Name == "Second");
        }
    }

    private static RasInfobaseSnapshot CreateSnapshot(
        Guid externalId,
        string name)
    {
        return new RasInfobaseSnapshot
        {
            ExternalId = externalId,
            Name = name,
            Description = $"Description for {name}"
        };
    }
}