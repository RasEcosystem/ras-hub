using RasHub.Contracts.Common.Pagination;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Infrastructure.IntegrationTests.Database;

namespace RasHub.Infrastructure.IntegrationTests.Queries;

public sealed class RasClusterQueriesTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Get_paged_scopes_clusters_to_gate_and_returns_stable_pages()
    {
        var firstGate = RasGateTestData.Create("First gate");
        var secondGate = RasGateTestData.Create("Second gate");
        var first = RasClusterTestData.Create(firstGate.Id, name: "A cluster");
        var second = RasClusterTestData.Create(firstGate.Id, name: "B cluster");
        var other = RasClusterTestData.Create(secondGate.Id, name: "Other cluster");

        await using var db = _database.CreateContext();
        db.RasGates.AddRange(firstGate, secondGate);
        db.RasClusters.AddRange(first, second, other);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var queries = new RasClusterQueries(db);

        var firstPage = await queries.GetPagedAsync(
            firstGate.Id,
            new PageRequest(1, 1),
            TestContext.Current.CancellationToken);
        var secondPage = await queries.GetPagedAsync(
            firstGate.Id,
            new PageRequest(2, 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(first.ExternalId, Assert.Single(firstPage.Items).Id);
        Assert.Equal(second.ExternalId, Assert.Single(secondPage.Items).Id);
        Assert.DoesNotContain(
            firstPage.Items.Concat(secondPage.Items),
            item => item.Id == other.ExternalId);
    }

    [Fact]
    public async Task Get_by_external_id_returns_cluster_only_from_requested_gate()
    {
        var firstGate = RasGateTestData.Create("First gate");
        var secondGate = RasGateTestData.Create("Second gate");
        var sharedExternalId = Guid.NewGuid();
        var expected = RasClusterTestData.Create(
            firstGate.Id,
            sharedExternalId,
            name: "Expected cluster");
        var other = RasClusterTestData.Create(
            secondGate.Id,
            sharedExternalId,
            name: "Other cluster");

        await using var db = _database.CreateContext();
        db.RasGates.AddRange(firstGate, secondGate);
        db.RasClusters.AddRange(expected, other);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var queries = new RasClusterQueries(db);

        var result = await queries.GetByExternalIdAsync(
            firstGate.Id,
            sharedExternalId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(expected.ExternalId, result.Id);
        Assert.Equal(expected.Name, result.Name);
    }
}
