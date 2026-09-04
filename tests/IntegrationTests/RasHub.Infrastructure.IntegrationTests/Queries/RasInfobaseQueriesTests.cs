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
    public async Task Get_paged_scopes_infobases_to_endpoint_and_cluster()
    {
        var gate = RasGateTestData.Create();
        var firstEndpoint = RasEndpointTestData.Create(gate.Id, "First RAS");
        var secondEndpoint = RasEndpointTestData.Create(gate.Id, "Second RAS");
        var firstCluster = RasClusterTestData.Create(firstEndpoint.Id);
        var siblingCluster = RasClusterTestData.Create(firstEndpoint.Id);
        var otherCluster = RasClusterTestData.Create(secondEndpoint.Id);
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
            gate,
            firstEndpoint,
            secondEndpoint,
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
            firstEndpoint.Id,
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
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var infobase = RasInfobaseTestData.Create(cluster.Id);

        await using var db = _database.CreateContext();
        db.AddRange(gate, endpoint, cluster, infobase);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        cluster.IsDeleted = true;
        cluster.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var queries = new RasInfobaseQueries(db);

        var result = await queries.GetByExternalIdAsync(
            endpoint.Id,
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
