using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks.Infobases;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.Domain;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.IntegrationTests.Database;

namespace RasHub.Infrastructure.IntegrationTests.RasGates.Tasks.Infobases;

public sealed class SynchronizeInfobasesTaskHandlerTests : IDisposable
{
    private static readonly DateTime ObservedAt = new(
        2026,
        8,
        20,
        12,
        0,
        0,
        DateTimeKind.Utc);

    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Execute_complete_snapshot_publishes_infobases_and_credentials()
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var remote = CreateSnapshot(Guid.NewGuid(), "rim_next");
        await SeedAsync(endpoint, rasGate, cluster);
        var gateway = new FakeRasInfobaseGateway
        {
            Infobases =
            [
                remote
            ]
        };

        await using (var db = _database.CreateContext())
        {
            var handler = CreateCollectionHandler(db, gateway);
            var task = new SynchronizeInfobasesTask(
                endpoint.Id,
                cluster.ExternalId,
                "cluster-admin",
                "cluster-secret");

            await handler.ExecuteAsync(
                task,
                TestContext.Current.CancellationToken);

            Assert.Equal(nameof(SynchronizeInfobasesTask), task.ToString());
            Assert.DoesNotContain("cluster-secret", task.ToString());
        }

        await using var verificationDb = _database.CreateContext();
        var stored = await verificationDb.RasInfobases.SingleAsync(
            TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);
        var storedEndpoint = await verificationDb.RasEndpoints.SingleAsync(
            item => item.Id == endpoint.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(remote.ExternalId, stored.ExternalId);
        Assert.Equal(remote.Name, stored.Name);
        Assert.Equal(ObservedAt, stored.ObservedAt);
        Assert.Null(storedGate.LastSeenAt);
        Assert.Equal(ObservedAt, storedEndpoint.LastSeenAt);
        Assert.Equal(cluster.ExternalId, gateway.RequestedClusterId);
        Assert.Equal("cluster-admin", gateway.ClusterUser);
        Assert.Equal("cluster-secret", gateway.ClusterPassword);
    }

    [Fact]
    public async Task Execute_targeted_sync_upserts_infobase_without_deleting_sibling()
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var sibling = RasInfobaseTestData.Create(cluster.Id, name: "Sibling");
        var remote = CreateSnapshot(Guid.NewGuid(), "Target");
        await SeedAsync(endpoint, rasGate, cluster, sibling);
        var gateway = new FakeRasInfobaseGateway { Infobase = remote };

        await using (var db = _database.CreateContext())
        {
            var handler = CreateTargetedHandler(db, gateway);

            await handler.ExecuteAsync(
                new SynchronizeInfobaseTask(
                    endpoint.Id,
                    cluster.ExternalId,
                    remote.ExternalId),
                TestContext.Current.CancellationToken);
        }

        await using var verificationDb = _database.CreateContext();
        var stored = await verificationDb.RasInfobases
            .OrderBy(infobase => infobase.Name)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, item => item.ExternalId == sibling.ExternalId);
        Assert.Contains(stored, item => item.ExternalId == remote.ExternalId);
        Assert.Equal(remote.ExternalId, gateway.RequestedInfobaseId);
    }

    [Fact]
    public async Task Execute_incomplete_snapshot_preserves_previous_infobases()
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var cluster = RasClusterTestData.Create(endpoint.Id);
        var existing = RasInfobaseTestData.Create(cluster.Id);
        await SeedAsync(endpoint, rasGate, cluster, existing);
        var gateway = new FakeRasInfobaseGateway { InfobaseSnapshotCompleteness = SnapshotCompleteness.Unknown };

        await using (var db = _database.CreateContext())
        {
            var handler = CreateCollectionHandler(db, gateway);

            await Assert.ThrowsAsync<RasGateClientException>(() =>
                handler.ExecuteAsync(
                    new SynchronizeInfobasesTask(
                        endpoint.Id,
                        cluster.ExternalId),
                    TestContext.Current.CancellationToken));
        }

        await using var verificationDb = _database.CreateContext();
        var stored = await verificationDb.RasInfobases.SingleAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(existing.ExternalId, stored.ExternalId);
        Assert.False(stored.IsDeleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execute_missing_cluster_throws_typed_non_retryable_exception(
        bool targeted)
    {
        var rasGate = RasGateTestData.Create();
        var endpoint = RasEndpointTestData.Create(rasGate.Id);
        var clusterId = Guid.NewGuid();
        await SeedAsync(endpoint, rasGate);
        await using var db = _database.CreateContext();
        var gateway = new FakeRasInfobaseGateway();

        var exception = await Assert.ThrowsAsync<RasClusterNotFoundException>(() =>
            ExecuteMissingClusterAsync(
                targeted,
                db,
                gateway,
                endpoint.Id,
                clusterId));

        Assert.Equal(endpoint.Id, exception.RasEndpointId);
        Assert.Equal(clusterId, exception.ClusterId);
        Assert.IsAssignableFrom<NonRetryableBackgroundTaskException>(exception);
    }

    private SynchronizeInfobasesTaskHandler CreateCollectionHandler(
        RasHubDbContext db,
        IRasInfobaseGateway gateway)
    {
        return new SynchronizeInfobasesTaskHandler(
            CreateTargetResolver(db),
            new EfRepository<RasCluster>(db),
            CreatePublisher(db),
            gateway,
            new FixedTimeProvider(ObservedAt));
    }

    private SynchronizeInfobaseTaskHandler CreateTargetedHandler(
        RasHubDbContext db,
        IRasInfobaseGateway gateway)
    {
        return new SynchronizeInfobaseTaskHandler(
            CreateTargetResolver(db),
            new EfRepository<RasCluster>(db),
            CreatePublisher(db),
            gateway,
            new FixedTimeProvider(ObservedAt));
    }

    private Task ExecuteMissingClusterAsync(
        bool targeted,
        RasHubDbContext db,
        IRasInfobaseGateway gateway,
        Guid rasEndpointId,
        Guid clusterId)
    {
        return targeted
            ? CreateTargetedHandler(db, gateway).ExecuteAsync(
                new SynchronizeInfobaseTask(
                    rasEndpointId,
                    clusterId,
                    Guid.NewGuid()),
                TestContext.Current.CancellationToken)
            : CreateCollectionHandler(db, gateway).ExecuteAsync(
                new SynchronizeInfobasesTask(rasEndpointId, clusterId),
                TestContext.Current.CancellationToken);
    }

    private static RasEndpointExecutionTargetResolver CreateTargetResolver(
        RasHubDbContext db)
    {
        return new RasEndpointExecutionTargetResolver(
            new EfRepository<RasEndpoint>(db),
            new EfRepository<RasGate>(db));
    }

    private static RasEndpointSyncPublisher CreatePublisher(RasHubDbContext db)
    {
        return new RasEndpointSyncPublisher(
            db,
            new RasClusterSnapshotStore(db),
            new RasInfobaseSnapshotStore(db));
    }

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = _database.CreateContext();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
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

    private sealed class FakeRasInfobaseGateway : IRasInfobaseGateway
    {
        public IReadOnlyList<RasInfobaseSnapshot> Infobases { get; init; } = [];

        public RasInfobaseSnapshot? Infobase { get; init; }

        public SnapshotCompleteness InfobaseSnapshotCompleteness { get; init; } =
            SnapshotCompleteness.Complete;

        public Guid? RequestedClusterId { get; private set; }

        public Guid? RequestedInfobaseId { get; private set; }

        public string? ClusterUser { get; private set; }

        public string? ClusterPassword { get; private set; }

        public Task<RasGateCapabilities> GetCapabilitiesAsync(
            RasGate rasGate,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new RasGateCapabilities
            {
                RacVersion = "8.3.27.2214",
                Resources =
                [
                    new RasResourceCapability("infobases", "info", 1),
                    new RasResourceCapability("infobases", "snapshot", 1)
                ]
            });
        }

        public Task<RasResourceSnapshot<RasInfobaseSnapshot>> GetInfobasesAsync(
            RasEndpointExecutionTarget target,
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            RequestedClusterId = clusterId;
            ClusterUser = clusterUser;
            ClusterPassword = clusterPassword;

            return Task.FromResult(
                new RasResourceSnapshot<RasInfobaseSnapshot>
                {
                    SchemaVersion = 1,
                    SourceVersion = "8.3.27.2214",
                    Completeness = InfobaseSnapshotCompleteness,
                    Items = Infobases
                });
        }

        public Task<RasInfobaseSnapshot> GetInfobaseAsync(
            RasEndpointExecutionTarget target,
            Guid clusterId,
            Guid infobaseId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            RequestedClusterId = clusterId;
            RequestedInfobaseId = infobaseId;
            ClusterUser = clusterUser;
            ClusterPassword = clusterPassword;

            return Task.FromResult(Infobase ?? throw new RasGateClientException(
                "No infobase result was configured."));
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(utcNow);
        }
    }
}
