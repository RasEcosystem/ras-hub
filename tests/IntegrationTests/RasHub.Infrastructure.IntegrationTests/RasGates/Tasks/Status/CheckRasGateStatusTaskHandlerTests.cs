using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks.Status;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.Domain;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.IntegrationTests.Database;

namespace RasHub.Infrastructure.IntegrationTests.RasGates.Tasks.Status;

public sealed class CheckRasGateStatusTaskHandlerTests : IDisposable
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
    public async Task Execute_unknown_RAC_observation_publishes_successful_gate_status()
    {
        var rasGate = RasGateTestData.Create();
        await using (var seedDb = _database.CreateContext())
        {
            seedDb.RasGates.Add(rasGate);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var executionDb = _database.CreateContext())
        {
            var handler = new CheckRasGateStatusTaskHandler(
                new EfRepository<RasGate>(executionDb),
                CreatePublisher(executionDb),
                new StubStatusGateway(
                    new RasGateStatus(
                        "Remote Gate",
                        "1.2.3")),
                new FixedTimeProvider(ObservedAt));

            await handler.ExecuteAsync(
                new CheckRasGateStatusTask(rasGate.Id),
                TestContext.Current.CancellationToken);
        }

        await using var verificationDb = _database.CreateContext();
        var stored = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal("Remote Gate", stored.InstanceName);
        Assert.Equal("1.2.3", stored.Version);
        Assert.Equal(ObservedAt, stored.StatusObservedAt);
        Assert.Null(stored.RacAvailable);
        Assert.Null(stored.RacVersion);
        Assert.Equal(ObservedAt, stored.RacStatusObservedAt);
        Assert.Equal(ObservedAt, stored.LastSeenAt);
    }

    [Fact]
    public async Task Execute_missing_gate_throws_typed_non_retryable_exception()
    {
        var rasGateId = Guid.NewGuid();
        await using var db = _database.CreateContext();
        var handler = new CheckRasGateStatusTaskHandler(
            new EfRepository<RasGate>(db),
            CreatePublisher(db),
            new StubStatusGateway(new RasGateStatus("Unused", "0.0.0")),
            new FixedTimeProvider(ObservedAt));

        var exception = await Assert.ThrowsAsync<RasGateNotFoundException>(() =>
            handler.ExecuteAsync(
                new CheckRasGateStatusTask(rasGateId),
                TestContext.Current.CancellationToken));

        Assert.Equal(rasGateId, exception.RasGateId);
        Assert.IsAssignableFrom<NonRetryableBackgroundTaskException>(exception);
    }

    private static RasGateSyncPublisher CreatePublisher(RasHubDbContext db)
    {
        return new RasGateSyncPublisher(
            db,
            new RasClusterSnapshotStore(db),
            new RasInfobaseSnapshotStore(db));
    }

    private sealed class StubStatusGateway(RasGateStatus status)
        : IRasGateStatusGateway
    {
        public Task<RasGateStatus> GetStatusAsync(
            RasGate rasGate,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(status);
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