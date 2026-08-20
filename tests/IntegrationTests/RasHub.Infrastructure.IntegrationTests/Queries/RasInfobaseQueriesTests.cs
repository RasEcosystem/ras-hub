using Microsoft.EntityFrameworkCore;
using RasHub.Contracts.Common.Pagination;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Infrastructure.IntegrationTests.Database;

namespace RasHub.Infrastructure.IntegrationTests.Queries;

public sealed class RasInfobaseQueriesTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Get_paged_scopes_infobases_to_gate_and_cluster()
    {
        var firstGate = RasGateTestData.Create("First gate");
        var secondGate = RasGateTestData.Create("Second gate");
        var firstCluster = RasClusterTestData.Create(firstGate.Id);
        var siblingCluster = RasClusterTestData.Create(firstGate.Id);
        var otherCluster = RasClusterTestData.Create(secondGate.Id);
        var first = RasInfobaseTestData.Create(
            firstCluster.Id,
            name: "A database");
        var second = RasInfobaseTestData.Create(
            firstCluster.Id,
            name: "B database");
        var sibling = RasInfobaseTestData.Create(siblingCluster.Id);
        var other = RasInfobaseTestData.Create(otherCluster.Id);

        await using var db = _database.CreateContext();
        db.AddRange(
            firstGate,
            secondGate,
            firstCluster,
            siblingCluster,
            otherCluster,
            first,
            second,
            sibling,
            other);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var queries = new RasInfobaseQueries(db);

        var result = await queries.GetPagedAsync(
            firstGate.Id,
            firstCluster.ExternalId,
            new PageRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(
            [first.ExternalId, second.ExternalId],
            result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Get_by_external_id_ignores_infobase_with_deleted_parent_cluster()
    {
        var rasGate = RasGateTestData.Create();
        var cluster = RasClusterTestData.Create(rasGate.Id);
        var infobase = RasInfobaseTestData.Create(cluster.Id);

        await using var db = _database.CreateContext();
        db.AddRange(rasGate, cluster, infobase);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        cluster.IsDeleted = true;
        cluster.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var queries = new RasInfobaseQueries(db);

        var result = await queries.GetByExternalIdAsync(
            rasGate.Id,
            cluster.ExternalId,
            infobase.ExternalId,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.NotNull(await db.RasInfobases
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.Id == infobase.Id,
                TestContext.Current.CancellationToken));
    }
}