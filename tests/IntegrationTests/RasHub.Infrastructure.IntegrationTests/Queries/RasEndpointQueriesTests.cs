using RasHub.Contracts.Common.Pagination;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Infrastructure.IntegrationTests.Database;

namespace RasHub.Infrastructure.IntegrationTests.Queries;

public sealed class RasEndpointQueriesTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Get_by_id_projects_public_fields_without_tracking()
    {
        var endpoint = RasEndpointTestData.Create();
        await using var db = _database.CreateContext();
        db.RasEndpoints.Add(endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var result = await new RasEndpointQueries(db).GetByIdAsync(
            endpoint.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(endpoint.Id, result.Id);
        Assert.Equal(endpoint.Name, result.Name);
        Assert.Equal(endpoint.Host, result.Host);
        Assert.Equal(endpoint.Port, result.Port);
        Assert.True(result.IsActive);
        Assert.Equal(endpoint.ConfigurationRevision, result.ConfigurationRevision);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Queries_exclude_soft_deleted_endpoints()
    {
        var active = RasEndpointTestData.Create("Active", "active.example.test");
        var deleted = RasEndpointTestData.Create("Deleted", "deleted.example.test");
        await using var db = _database.CreateContext();
        db.RasEndpoints.AddRange(active, deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.RasEndpoints.Remove(deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var queries = new RasEndpointQueries(db);

        var page = await queries.GetPagedAsync(
            new PageRequest(1, 20),
            TestContext.Current.CancellationToken);
        var administrationItems = await queries.GetAdministrationItemsAsync(
            true,
            TestContext.Current.CancellationToken);

        Assert.Equal(active.Id, Assert.Single(page.Items).Id);
        Assert.Null(await queries.GetByIdAsync(
            deleted.Id,
            TestContext.Current.CancellationToken));
        Assert.Equal(2, administrationItems.Count);
        Assert.True(Assert.Single(
            administrationItems,
            item => item.Id == deleted.Id).IsDeleted);
    }
}
