using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Domain;
using RasHub.Infrastructure.Database;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasEndpointRegistryTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Theory]
    [InlineData(" RAS.EXAMPLE.TEST. ", "ras.example.test")]
    [InlineData("[2001:0db8::1]", "2001:db8::1")]
    public async Task Register_normalizes_valid_host(
        string host,
        string expectedHost)
    {
        await using var db = _database.CreateContext();
        var gate = RasGateTestData.Create();
        db.RasGates.Add(gate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = CreateRegistry(db);

        var endpoint = await registry.RegisterAsync(
            new RasEndpointRegistration(
                " Production ",
                gate.Id,
                host,
                1545,
                true),
            TestContext.Current.CancellationToken);

        Assert.Equal("Production", endpoint.Name);
        Assert.Equal(expectedHost, endpoint.Host);
        Assert.Equal(1545, endpoint.Port);
        Assert.Equal(1, endpoint.ConfigurationRevision);
    }

    [Theory]
    [InlineData("https://ras.example.test")]
    [InlineData("ras.example.test/path")]
    [InlineData("host name")]
    public async Task Register_rejects_non_host_value(string host)
    {
        await using var db = _database.CreateContext();
        var gate = RasGateTestData.Create();
        db.RasGates.Add(gate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = CreateRegistry(db);

        await Assert.ThrowsAsync<RasEndpointAddressValidationException>(() =>
            registry.RegisterAsync(
                new RasEndpointRegistration(
                    "RAS",
                    gate.Id,
                    host,
                    1545,
                    true),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_with_stale_revision_preserves_current_configuration()
    {
        await using var db = _database.CreateContext();
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        db.AddRange(gate, endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = CreateRegistry(db);

        await registry.UpdateAsync(
            endpoint.Id,
            new RasEndpointRegistrationUpdate(
                "First update",
                gate.Id,
                endpoint.Host,
                endpoint.Port,
                endpoint.IsActive,
                endpoint.ConfigurationRevision),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<RasEndpointRevisionConflictException>(() =>
            registry.UpdateAsync(
                endpoint.Id,
                new RasEndpointRegistrationUpdate(
                    "Stale update",
                    gate.Id,
                    endpoint.Host,
                    endpoint.Port,
                    endpoint.IsActive,
                    1),
                TestContext.Current.CancellationToken));

        Assert.Equal("First update", endpoint.Name);
        Assert.Equal(2, endpoint.ConfigurationRevision);
    }

    [Fact]
    public async Task Update_remote_identity_invalidates_endpoint_shadow_and_observation()
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        endpoint.LastSeenAt = DateTime.UtcNow.AddMinutes(-1);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var infobase = RasInfobaseTestData.Create(cluster.Id);
        await using var db = _database.CreateContext();
        db.AddRange(gate, endpoint, cluster, infobase);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = CreateRegistry(db);

        await registry.UpdateAsync(
            endpoint.Id,
            new RasEndpointRegistrationUpdate(
                endpoint.Name,
                gate.Id,
                "replacement.example.test",
                2545,
                true,
                endpoint.ConfigurationRevision),
            TestContext.Current.CancellationToken);

        Assert.Equal("replacement.example.test", endpoint.Host);
        Assert.Equal(2545, endpoint.Port);
        Assert.Equal(2, endpoint.ConfigurationRevision);
        Assert.Null(endpoint.LastSeenAt);
        Assert.True((await db.RasClusters
            .IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken)).IsDeleted);
        Assert.True((await db.RasInfobases
            .IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken)).IsDeleted);
    }

    [Fact]
    public async Task Update_execution_gate_preserves_endpoint_shadow_and_observation()
    {
        var originalGate = RasGateTestData.Create("Original Gate");
        var replacementGate = RasGateTestData.Create("Replacement Gate");
        var endpoint = RasEndpointTestData.Create(originalGate.Id);
        endpoint.LastSeenAt = DateTime.UtcNow.AddMinutes(-1);
        var observedAt = endpoint.LastSeenAt;
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var infobase = RasInfobaseTestData.Create(cluster.Id);
        await using var db = _database.CreateContext();
        db.AddRange(
            originalGate,
            replacementGate,
            endpoint,
            cluster,
            infobase);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = CreateRegistry(db);

        await registry.UpdateAsync(
            endpoint.Id,
            new RasEndpointRegistrationUpdate(
                endpoint.Name,
                replacementGate.Id,
                endpoint.Host,
                endpoint.Port,
                endpoint.IsActive,
                endpoint.ConfigurationRevision),
            TestContext.Current.CancellationToken);

        Assert.Equal(replacementGate.Id, endpoint.RasGateId);
        Assert.Equal(2, endpoint.ConfigurationRevision);
        Assert.Equal(observedAt, endpoint.LastSeenAt);
        Assert.False((await db.RasClusters.SingleAsync(
            TestContext.Current.CancellationToken)).IsDeleted);
        Assert.False((await db.RasInfobases.SingleAsync(
            TestContext.Current.CancellationToken)).IsDeleted);
    }

    [Fact]
    public async Task Unregister_and_restore_by_id_keep_endpoint_shadow_invalidated()
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        endpoint.LastSeenAt = DateTime.UtcNow.AddMinutes(-1);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var infobase = RasInfobaseTestData.Create(cluster.Id);
        await using var db = _database.CreateContext();
        db.AddRange(gate, endpoint, cluster, infobase);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = CreateRegistry(db);

        var removed = await registry.UnregisterAsync(
            endpoint.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(removed);
        Assert.True(removed.IsDeleted);
        Assert.Equal(2, removed.ConfigurationRevision);
        Assert.Null(removed.LastSeenAt);

        db.ChangeTracker.Clear();

        var restored = await registry.RestoreAsync(
            endpoint.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(restored);
        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeletedAt);
        Assert.Equal(3, restored.ConfigurationRevision);
        Assert.Null(restored.LastSeenAt);
        Assert.True((await db.RasClusters
            .IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken)).IsDeleted);
        Assert.True((await db.RasInfobases
            .IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken)).IsDeleted);
    }

    private static RasEndpointRegistry CreateRegistry(RasHubDbContext db)
    {
        return new RasEndpointRegistry(
            new EfRepository<RasEndpoint>(db),
            new EfRepository<RasGate>(db),
            new RasClusterSnapshotStore(db),
            db);
    }
}
