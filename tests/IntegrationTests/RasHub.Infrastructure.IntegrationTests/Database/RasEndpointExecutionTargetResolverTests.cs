using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Domain;
using RasHub.Infrastructure.Database;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasEndpointExecutionTargetResolverTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Resolve_active_endpoint_returns_assigned_gate_and_address()
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(
            gate.Id,
            host: "2001:db8::1",
            port: 2545);
        await using var db = _database.CreateContext();
        db.AddRange(gate, endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var resolver = CreateResolver(db);

        var result = await resolver.ResolveAsync(
            endpoint.Id,
            TestContext.Current.CancellationToken);

        Assert.Same(endpoint, result.Endpoint);
        Assert.Same(gate, result.Gate);
        Assert.Equal("[2001:db8::1]:2545", result.Address.ToString());
    }

    [Fact]
    public async Task Resolve_inactive_endpoint_rejects_execution()
    {
        var gate = RasGateTestData.Create();
        var inactive = RasEndpointTestData.Create(
            gate.Id,
            "Inactive",
            "inactive.example.test");
        inactive.IsActive = false;
        await using var db = _database.CreateContext();
        db.AddRange(gate, inactive);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var resolver = CreateResolver(db);

        await Assert.ThrowsAsync<RasEndpointInactiveException>(() =>
            resolver.ResolveAsync(
                inactive.Id,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Resolve_inactive_assigned_gate_rejects_execution()
    {
        var gate = RasGateTestData.Create();
        gate.IsActive = false;
        var endpoint = RasEndpointTestData.Create(gate.Id);
        await using var db = _database.CreateContext();
        db.AddRange(gate, endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var resolver = CreateResolver(db);

        var exception = await Assert.ThrowsAsync<
            RasEndpointGateUnavailableException>(() =>
            resolver.ResolveAsync(
                endpoint.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal(endpoint.Id, exception.RasEndpointId);
        Assert.Equal(gate.Id, exception.RasGateId);
    }

    [Fact]
    public async Task Resolve_unknown_endpoint_rejects_execution()
    {
        await using var db = _database.CreateContext();
        var resolver = CreateResolver(db);

        await Assert.ThrowsAsync<RasEndpointNotFoundException>(() =>
            resolver.ResolveAsync(
                Guid.NewGuid(),
                TestContext.Current.CancellationToken));
    }

    private static RasEndpointExecutionTargetResolver CreateResolver(
        RasHubDbContext db)
    {
        return new RasEndpointExecutionTargetResolver(
            new EfRepository<RasEndpoint>(db),
            new EfRepository<RasGate>(db));
    }
}
