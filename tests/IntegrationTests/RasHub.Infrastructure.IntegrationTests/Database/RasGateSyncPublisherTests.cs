using Microsoft.EntityFrameworkCore;
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
        var publisher = CreatePublisher(publicationDb);

        var published = await publisher.TryPublishStatusAsync(
            rasGate.Id,
            1,
            new RasGateStatus("stale-instance", "stale-version"),
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
        Assert.Null(stored.LastSeenAt);
    }

    [Fact]
    public async Task Publish_snapshot_when_revision_changed_rolls_back_complete_mutation()
    {
        var rasGate = RasGateTestData.Create();
        var existingCluster = RasClusterTestData.Create(rasGate.Id);

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(rasGate, existingCluster);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await ChangeGateAsync(rasGate.Id, "reconfigured");
        var publisher = CreatePublisher(publicationDb);
        var replacementId = Guid.NewGuid();

        var published = await publisher.TryPublishClustersAsync(
            rasGate.Id,
            1,
            [CreateSnapshot(replacementId)],
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var clusters = await verificationDb.RasClusters
            .IgnoreQueryFilters()
            .Where(item => item.RasGateId == rasGate.Id)
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
        var existingCluster = RasClusterTestData.Create(rasGate.Id);

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(rasGate, existingCluster);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await ChangeGateAsync(rasGate.Id, "reconfigured");
        var publisher = CreatePublisher(publicationDb);

        var published = await publisher.TryPublishClusterAsync(
            rasGate.Id,
            1,
            CreateSnapshot(existingCluster.ExternalId),
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var storedCluster = await verificationDb.RasClusters.SingleAsync(
            item => item.RasGateId == rasGate.Id &&
                    item.ExternalId == existingCluster.ExternalId,
            TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(existingCluster.Name, storedCluster.Name);
        Assert.Null(storedGate.LastSeenAt);
    }

    [Fact]
    public async Task Remove_cluster_when_revision_changed_rolls_back_removal()
    {
        var rasGate = RasGateTestData.Create();
        var existingCluster = RasClusterTestData.Create(rasGate.Id);

        await using (var seedDb = _database.CreateContext())
        {
            seedDb.AddRange(rasGate, existingCluster);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var publicationDb = _database.CreateContext();
        _ = await publicationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        await ChangeGateAsync(rasGate.Id, "reconfigured");
        var publisher = CreatePublisher(publicationDb);

        var published = await publisher.TryRemoveClusterAsync(
            rasGate.Id,
            1,
            existingCluster.ExternalId,
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.False(published);

        await using var verificationDb = _database.CreateContext();
        var storedCluster = await verificationDb.RasClusters.SingleAsync(
            item => item.RasGateId == rasGate.Id &&
                    item.ExternalId == existingCluster.ExternalId,
            TestContext.Current.CancellationToken);
        var storedGate = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.False(storedCluster.IsDeleted);
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

    private static RasGateSyncPublisher CreatePublisher(RasHubDbContext db)
    {
        return new RasGateSyncPublisher(
            db,
            new RasClusterSnapshotStore(db));
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
}