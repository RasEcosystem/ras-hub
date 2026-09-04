using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;
using RasHub.Domain.Enums;
using RasHub.Infrastructure.Database;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasGateSyncPublisherTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Publish_status_stores_gate_and_RAC_observations_atomically()
    {
        var observedAt = new DateTime(
            2026,
            8,
            20,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var rasGate = RasGateTestData.Create();
        await SeedGateAsync(rasGate);

        await using (var publicationDb = _database.CreateContext())
        {
            var publisher = CreateGatePublisher(publicationDb);

            var published = await publisher.TryPublishStatusAsync(
                rasGate.Id,
                rasGate.ConfigurationRevision,
                new RasGateStatus(
                    "Remote Gate",
                    "1.2.3",
                    false),
                observedAt,
                TestContext.Current.CancellationToken);

            Assert.True(published);
        }

        await using var verificationDb = _database.CreateContext();
        var stored = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal("Remote Gate", stored.InstanceName);
        Assert.Equal("1.2.3", stored.Version);
        Assert.Equal(observedAt, stored.StatusObservedAt);
        Assert.False(stored.RacAvailable);
        Assert.Null(stored.RacVersion);
        Assert.Equal(observedAt, stored.RacStatusObservedAt);
        Assert.Equal(observedAt, stored.LastSeenAt);
    }

    [Theory]
    [InlineData("reconfigured")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    public async Task Publish_status_when_gate_changed_during_request_discards_result(
        string change)
    {
        var rasGate = RasGateTestData.Create();
        await SeedGateAsync(rasGate);

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await ChangeGateAsync(rasGate.Id, change);
        var publisher = CreateGatePublisher(publicationDb);

        var published = await publisher.TryPublishStatusAsync(
            rasGate.Id,
            1,
            new RasGateStatus(
                "stale-instance",
                "stale-version",
                true,
                "8.3.27.2214"),
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var stored = await verificationDb.RasGates
            .IgnoreQueryFilters()
            .SingleAsync(
                item => item.Id == rasGate.Id,
                TestContext.Current.CancellationToken);
        Assert.Null(stored.InstanceName);
        Assert.Null(stored.Version);
        Assert.Null(stored.StatusObservedAt);
        Assert.Null(stored.RacAvailable);
        Assert.Null(stored.RacVersion);
        Assert.Null(stored.RacStatusObservedAt);
        Assert.Null(stored.LastSeenAt);
    }

    [Fact]
    public async Task Publish_status_after_delete_and_restore_discards_pre_delete_result()
    {
        var rasGate = RasGateTestData.Create();
        await SeedGateAsync(rasGate);

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await using (var lifecycleDb = _database.CreateContext())
        {
            var lifecycleGate = await lifecycleDb.RasGates.SingleAsync(
                item => item.Id == rasGate.Id,
                TestContext.Current.CancellationToken);
            lifecycleDb.RasGates.Remove(lifecycleGate);
            await lifecycleDb.SaveChangesAsync(
                TestContext.Current.CancellationToken);

            lifecycleGate.IsDeleted = false;
            lifecycleGate.DeletedAt = null;
            await lifecycleDb.SaveChangesAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(3, lifecycleGate.ConfigurationRevision);
        }

        var publisher = CreateGatePublisher(publicationDb);
        var published = await publisher.TryPublishStatusAsync(
            rasGate.Id,
            1,
            new RasGateStatus(
                "pre-delete-instance",
                "pre-delete-version",
                true,
                "8.3.27.2214"),
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var stored = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(3, stored.ConfigurationRevision);
        Assert.Null(stored.InstanceName);
        Assert.Null(stored.Version);
        Assert.Null(stored.StatusObservedAt);
        Assert.Null(stored.RacAvailable);
        Assert.Null(stored.RacVersion);
        Assert.Null(stored.RacStatusObservedAt);
        Assert.Null(stored.LastSeenAt);
    }

    [Fact]
    public async Task Publish_snapshot_preserves_gate_observation_owned_by_status_publisher()
    {
        var endpointObservedAt = new DateTime(
            2026,
            8,
            20,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var gateObservedAt = endpointObservedAt.AddMinutes(1);
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        rasGate.LastSeenAt = gateObservedAt;

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(endpoint, rasGate);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var publicationDb = _database.CreateContext())
        {
            var publisher = CreateEndpointPublisher(publicationDb);
            var published = await publisher.TryPublishClustersAsync(
                CreateGuard(endpoint, rasGate),
                [CreateSnapshot(Guid.NewGuid())],
                endpointObservedAt,
                TestContext.Current.CancellationToken);

            Assert.True(published);
        }

        await using var verificationDb = _database.CreateContext();
        var storedEndpoint = await verificationDb.RasEndpoints.SingleAsync(
            item => item.Id == endpoint.Id,
            TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(endpointObservedAt, storedEndpoint.LastSeenAt);
        Assert.Equal(gateObservedAt, storedGate.LastSeenAt);
    }

    [Fact]
    public async Task Publish_snapshot_when_endpoint_revision_changed_rolls_back_complete_mutation()
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var existingCluster = RasClusterTestData.Create(endpoint.Id);
        var guard = CreateGuard(endpoint, rasGate);

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(endpoint, rasGate, existingCluster);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasEndpoints.SingleAsync(
            item => item.Id == endpoint.Id,
            TestContext.Current.CancellationToken);

        await ChangeEndpointAsync(endpoint.Id);
        var publisher = CreateEndpointPublisher(publicationDb);

        var published = await publisher.TryPublishClustersAsync(
            guard,
            [CreateSnapshot(Guid.NewGuid())],
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var storedCluster = await verificationDb.RasClusters.SingleAsync(
            item => item.RasEndpointId == endpoint.Id,
            TestContext.Current.CancellationToken);
        var storedEndpoint = await verificationDb.RasEndpoints.SingleAsync(
            item => item.Id == endpoint.Id,
            TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(existingCluster.ExternalId, storedCluster.ExternalId);
        Assert.Equal("replacement-ras.example.test", storedEndpoint.Host);
        Assert.Equal(2, storedEndpoint.ConfigurationRevision);
        Assert.Null(storedEndpoint.LastSeenAt);
        Assert.Null(storedGate.LastSeenAt);
    }

    [Fact]
    public async Task Publish_snapshot_when_revision_changed_rolls_back_complete_mutation()
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var existingCluster = RasClusterTestData.Create(endpoint.Id);

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(endpoint, rasGate, existingCluster);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await ChangeGateAsync(rasGate.Id, "reconfigured");
        var publisher = CreateEndpointPublisher(publicationDb);
        var replacementId = Guid.NewGuid();

        var published = await publisher.TryPublishClustersAsync(
            CreateGuard(endpoint, rasGate),
            [CreateSnapshot(replacementId)],
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var clusters = await verificationDb.RasClusters
            .IgnoreQueryFilters()
            .Where(item => item.RasEndpointId == endpoint.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        var storedCluster = Assert.Single(clusters);
        Assert.Equal(existingCluster.ExternalId, storedCluster.ExternalId);
        Assert.False(storedCluster.IsDeleted);
        Assert.Null(storedGate.LastSeenAt);
    }

    [Fact]
    public async Task Publish_cluster_when_revision_changed_rolls_back_targeted_mutation()
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var existingCluster = RasClusterTestData.Create(endpoint.Id);

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(endpoint, rasGate, existingCluster);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await ChangeGateAsync(rasGate.Id, "reconfigured");
        var publisher = CreateEndpointPublisher(publicationDb);

        var published = await publisher.TryPublishClusterAsync(
            CreateGuard(endpoint, rasGate),
            CreateSnapshot(existingCluster.ExternalId),
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var storedCluster = await verificationDb.RasClusters.SingleAsync(
            item => item.RasEndpointId == endpoint.Id &&
                    item.ExternalId == existingCluster.ExternalId,
            TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(existingCluster.Name, storedCluster.Name);
        Assert.Null(storedGate.LastSeenAt);
    }

    [Fact]
    public async Task Publish_infobases_when_revision_changed_rolls_back_snapshot()
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var existing = RasInfobaseTestData.Create(cluster.Id);

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(endpoint, rasGate, cluster, existing);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await ChangeGateAsync(rasGate.Id, "reconfigured");
        var publisher = CreateEndpointPublisher(publicationDb);

        var published = await publisher.TryPublishInfobasesAsync(
            CreateGuard(endpoint, rasGate),
            cluster.ExternalId,
            [CreateInfobaseSnapshot(Guid.NewGuid())],
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var stored = await verificationDb.RasInfobases
            .IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(existing.ExternalId, stored.ExternalId);
        Assert.False(stored.IsDeleted);
        Assert.Null(storedGate.LastSeenAt);
    }

    [Fact]
    public async Task Remove_cluster_when_revision_changed_rolls_back_removal()
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var existingCluster = RasClusterTestData.Create(endpoint.Id);

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(endpoint, rasGate, existingCluster);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await ChangeGateAsync(rasGate.Id, "reconfigured");
        var publisher = CreateEndpointPublisher(publicationDb);

        var published = await publisher.TryRemoveClusterAsync(
            CreateGuard(endpoint, rasGate),
            existingCluster.ExternalId,
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var storedCluster = await verificationDb.RasClusters.SingleAsync(
            item => item.RasEndpointId == endpoint.Id &&
                    item.ExternalId == existingCluster.ExternalId,
            TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.False(storedCluster.IsDeleted);
        Assert.Null(storedGate.LastSeenAt);
    }

    [Fact]
    public async Task Remove_infobase_when_revision_changed_rolls_back_removal()
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var infobase = RasInfobaseTestData.Create(cluster.Id);

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(endpoint, rasGate, cluster, infobase);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await ChangeGateAsync(rasGate.Id, "reconfigured");
        var publisher = CreateEndpointPublisher(publicationDb);

        var published = await publisher.TryRemoveInfobaseAsync(
            CreateGuard(endpoint, rasGate),
            cluster.ExternalId,
            infobase.ExternalId,
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var storedInfobase = await verificationDb.RasInfobases.SingleAsync(
            item => item.RasClusterId == cluster.Id &&
                    item.ExternalId == infobase.ExternalId,
            TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.False(storedInfobase.IsDeleted);
        Assert.Null(storedGate.LastSeenAt);
    }

    private async Task SeedGateAsync(RasGate rasGate)
    {
        await using var db = _database.CreateContext();
        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ChangeGateAsync(Guid rasGateId, string change)
    {
        await using var db = _database.CreateContext();
        var rasGate = await db.RasGates.SingleAsync(
            item => item.Id == rasGateId,
            TestContext.Current.CancellationToken);

        switch (change)
        {
            case "reconfigured":
                rasGate.Url = "https://replacement.example.test";
                rasGate.ApiKey = "replacement-secret";
                break;
            case "inactive":
                rasGate.IsActive = false;
                break;
            case "deleted":
                db.RasGates.Remove(rasGate);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ChangeEndpointAsync(Guid rasEndpointId)
    {
        await using var db = _database.CreateContext();
        var endpoint = await db.RasEndpoints.SingleAsync(
            item => item.Id == rasEndpointId,
            TestContext.Current.CancellationToken);
        endpoint.Host = "replacement-ras.example.test";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static RasGateSyncPublisher CreateGatePublisher(RasHubDbContext db)
    {
        return new RasGateSyncPublisher(db);
    }

    private static RasEndpointSyncPublisher CreateEndpointPublisher(
        RasHubDbContext db)
    {
        return new RasEndpointSyncPublisher(
            db,
            new RasClusterSnapshotStore(db),
            new RasInfobaseSnapshotStore(db));
    }

    private static RasEndpointExecutionGuard CreateGuard(
        RasEndpoint endpoint,
        RasGate rasGate)
    {
        return new RasEndpointExecutionGuard(
            endpoint.Id,
            endpoint.ConfigurationRevision,
            rasGate.Id,
            rasGate.ConfigurationRevision);
    }

    private static RasClusterSnapshot CreateSnapshot(Guid externalId)
    {
        return new RasClusterSnapshot
        {
            ExternalId = externalId,
            Name = "Replacement",
            Host = "replacement-host",
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

    private static RasInfobaseSnapshot CreateInfobaseSnapshot(Guid externalId)
    {
        return new RasInfobaseSnapshot
        {
            ExternalId = externalId,
            Name = "Replacement infobase",
            Description = "Replacement description"
        };
    }
}
