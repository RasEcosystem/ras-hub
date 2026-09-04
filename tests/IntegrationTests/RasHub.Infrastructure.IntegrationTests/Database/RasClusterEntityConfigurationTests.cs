using Microsoft.EntityFrameworkCore;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasClusterEntityConfigurationTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Cluster_round_trips_all_synchronized_fields()
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        var cluster = RasClusterTestData.Create(endpoint.Id);

        await using (var db = _database.CreateContext())
        {
            db.AddRange(gate, endpoint);
            db.RasClusters.Add(cluster);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verificationDb = _database.CreateContext();
        var stored = await verificationDb.RasClusters.SingleAsync(
            item => item.Id == cluster.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(endpoint.Id, stored.RasEndpointId);
        Assert.Equal(cluster.ExternalId, stored.ExternalId);
        Assert.Equal(cluster.Name, stored.Name);
        Assert.Equal(cluster.Host, stored.Host);
        Assert.Equal(cluster.Port, stored.Port);
        Assert.Equal(cluster.ExpirationTimeoutSeconds, stored.ExpirationTimeoutSeconds);
        Assert.Equal(cluster.LifetimeLimitSeconds, stored.LifetimeLimitSeconds);
        Assert.Equal(cluster.MaxMemorySizeKb, stored.MaxMemorySizeKb);
        Assert.Equal(cluster.MaxMemoryTimeLimitSeconds, stored.MaxMemoryTimeLimitSeconds);
        Assert.Equal(cluster.SecurityLevel, stored.SecurityLevel);
        Assert.Equal(
            cluster.SessionFaultToleranceLevel,
            stored.SessionFaultToleranceLevel);
        Assert.Equal(cluster.LoadBalancingMode, stored.LoadBalancingMode);
        Assert.Equal(
            cluster.ErrorsCountThresholdPercent,
            stored.ErrorsCountThresholdPercent);
        Assert.Equal(cluster.KillProblemProcesses, stored.KillProblemProcesses);
        Assert.Equal(cluster.KillByMemoryWithDump, stored.KillByMemoryWithDump);
        Assert.Equal(
            cluster.AllowAccessRightAuditEventsRecording,
            stored.AllowAccessRightAuditEventsRecording);
        Assert.Equal(cluster.PingPeriod, stored.PingPeriod);
        Assert.Equal(cluster.PingTimeout, stored.PingTimeout);
        Assert.Equal(cluster.RestartSchedule, stored.RestartSchedule);
        Assert.Equal(cluster.ObservedAt, stored.ObservedAt);
        Assert.NotEqual(default, stored.CreatedAt);
        Assert.NotEqual(default, stored.UpdatedAt);
    }

    [Fact]
    public async Task Same_external_id_is_unique_within_a_RasEndpoint()
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);
        var externalId = Guid.NewGuid();

        await using var db = _database.CreateContext();
        db.AddRange(gate, endpoint);
        db.RasClusters.AddRange(
            RasClusterTestData.Create(endpoint.Id, externalId),
            RasClusterTestData.Create(endpoint.Id, externalId));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_536)]
    public async Task Database_rejects_ports_outside_the_valid_range(int port)
    {
        var gate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(gate.Id);

        await using var db = _database.CreateContext();
        db.AddRange(gate, endpoint);
        db.RasClusters.Add(RasClusterTestData.Create(endpoint.Id, port: port));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}
