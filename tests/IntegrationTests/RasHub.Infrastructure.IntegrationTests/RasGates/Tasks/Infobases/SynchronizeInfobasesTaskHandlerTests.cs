using Microsoft.EntityFrameworkCore;
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
        var cluster = RasClusterTestData.Create(rasGate.Id);
        var remote = CreateSnapshot(Guid.NewGuid(), "rim_next");
        await SeedAsync(rasGate, cluster);
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
                rasGate.Id,
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

        Assert.Equal(remote.ExternalId, stored.ExternalId);
        Assert.Equal(remote.Name, stored.Name);
        Assert.Equal(ObservedAt, stored.ObservedAt);
        Assert.Equal(ObservedAt, storedGate.LastSeenAt);
        Assert.Equal(cluster.ExternalId, gateway.RequestedClusterId);
        Assert.Equal("cluster-admin", gateway.ClusterUser);
        Assert.Equal("cluster-secret", gateway.ClusterPassword);
    }

    [Fact]
    public async Task Execute_targeted_sync_upserts_infobase_without_deleting_sibling()
    {
        var rasGate = RasGateTestData.Create();
        var cluster = RasClusterTestData.Create(rasGate.Id);
        var sibling = RasInfobaseTestData.Create(cluster.Id, name: "Sibling");
        var remote = CreateSnapshot(Guid.NewGuid(), "Target");
        await SeedAsync(rasGate, cluster, sibling);
        var gateway = new FakeRasInfobaseGateway { Infobase = remote };

        await using (var db = _database.CreateContext())
        {
            var handler = CreateTargetedHandler(db, gateway);

            await handler.ExecuteAsync(
                new SynchronizeInfobaseTask(
                    rasGate.Id,
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
        var cluster = RasClusterTestData.Create(rasGate.Id);
        var existing = RasInfobaseTestData.Create(cluster.Id);
        await SeedAsync(rasGate, cluster, existing);
        var gateway = new FakeRasInfobaseGateway { InfobaseSnapshotCompleteness = SnapshotCompleteness.Unknown };

        await using (var db = _database.CreateContext())
        {
            var handler = CreateCollectionHandler(db, gateway);

            await Assert.ThrowsAsync<RasGateClientException>(() =>
                handler.ExecuteAsync(
                    new SynchronizeInfobasesTask(
                        rasGate.Id,
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
        var clusterId = Guid.NewGuid();
        await SeedAsync(rasGate);
        await using var db = _database.CreateContext();
        var gateway = new FakeRasInfobaseGateway();

        var exception = await Assert.ThrowsAsync<RasClusterNotFoundException>(() =>
            ExecuteMissingClusterAsync(
                targeted,
                db,
                gateway,
                rasGate.Id,
                clusterId));

        Assert.Equal(rasGate.Id, exception.RasGateId);
        Assert.Equal(clusterId, exception.ClusterId);
        Assert.IsAssignableFrom<NonRetryableBackgroundTaskException>(exception);
    }

    private SynchronizeInfobasesTaskHandler CreateCollectionHandler(
        RasHubDbContext db,
        IRasInfobaseGateway gateway)
    {
        return new SynchronizeInfobasesTaskHandler(
            new EfRepository<RasGate>(db),
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
            new EfRepository<RasGate>(db),
            new EfRepository<RasCluster>(db),
            CreatePublisher(db),
            gateway,
            new FixedTimeProvider(ObservedAt));
    }

    private Task ExecuteMissingClusterAsync(
        bool targeted,
        RasHubDbContext db,
        IRasInfobaseGateway gateway,
        Guid rasGateId,
        Guid clusterId)
    {
        return targeted
            ? CreateTargetedHandler(db, gateway).ExecuteAsync(
                new SynchronizeInfobaseTask(
                    rasGateId,
                    clusterId,
                    Guid.NewGuid()),
                TestContext.Current.CancellationToken)
            : CreateCollectionHandler(db, gateway).ExecuteAsync(
                new SynchronizeInfobasesTask(rasGateId, clusterId),
                TestContext.Current.CancellationToken);
    }

    private static RasGateSyncPublisher CreatePublisher(RasHubDbContext db)
    {
        return new RasGateSyncPublisher(
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
            RasGate rasGate,
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
            RasGate rasGate,
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