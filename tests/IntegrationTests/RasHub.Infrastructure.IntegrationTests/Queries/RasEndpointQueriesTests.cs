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
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        await using var db = _database.CreateContext();
        db.AddRange(gate, endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var result = await new RasEndpointQueries(db).GetByIdAsync(
            endpoint.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(endpoint.Id, result.Id);
        Assert.Equal(gate.Id, result.RasGateId);
        Assert.Equal(endpoint.Name, result.Name);
        Assert.Equal(endpoint.Host, result.Host);
        Assert.Equal(endpoint.Port, result.Port);
        Assert.True(result.IsActive);
        Assert.Equal(endpoint.ConfigurationRevision, result.ConfigurationRevision);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Get_administration_items_projects_endpoint_and_gate_fields()
    {
        var gate = RasGateTestData.Create(
            "Execution gate",
            "https://execution-gate.example.test",
            8443);
        gate.IsActive = false;
        var endpoint = RasEndpointTestData.Create(
            gate.Id);

        await using var db = _database.CreateContext();
        db.AddRange(gate, endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var result = await new RasEndpointQueries(db)
            .GetAdministrationItemsAsync(
                false,
                TestContext.Current.CancellationToken);

        var item = Assert.Single(result);
        Assert.Equal(endpoint.Id, item.Id);
        Assert.Equal(endpoint.Name, item.Name);
        Assert.Equal(gate.Id, item.RasGateId);
        Assert.Equal(gate.Name, item.RasGateName);
        Assert.Equal(gate.Url, item.RasGateUrl);
        Assert.Equal(gate.Port, item.RasGatePort);
        Assert.False(item.RasGateIsActive);
        Assert.False(item.RasGateIsDeleted);
        Assert.Equal(endpoint.Host, item.Host);
        Assert.Equal(endpoint.Port, item.Port);
        Assert.True(item.IsActive);
        Assert.Equal(
            endpoint.ConfigurationRevision,
            item.ConfigurationRevision);
        Assert.Equal(endpoint.CreatedAt, item.CreatedAt);
        Assert.Equal(endpoint.UpdatedAt, item.UpdatedAt);
        Assert.False(item.IsDeleted);
        Assert.Null(item.DeletedAt);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Queries_exclude_soft_deleted_endpoints()
    {
        var gate = RasGateTestData.Create();
        var active = RasEndpointTestData.Create(
            gate.Id,
            "Active",
            "active.example.test");
        var deleted = RasEndpointTestData.Create(
            gate.Id,
            "Deleted",
            "deleted.example.test");
        await using var db = _database.CreateContext();
        db.RasGates.Add(gate);
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
