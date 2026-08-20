using Microsoft.EntityFrameworkCore;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class AuditSoftDeleteInterceptorTests : IDisposable
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteRasHubDatabase _database;
    private readonly ManualTimeProvider _timeProvider = new(InitialTime);

    public AuditSoftDeleteInterceptorTests()
    {
        _database = new SqliteRasHubDatabase(_timeProvider);
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Insert_sets_created_and_updated_timestamps()
    {
        await using var db = _database.CreateContext();
        var rasGate = RasGateTestData.Create();
        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InitialTime.UtcDateTime, rasGate.CreatedAt);
        Assert.Equal(rasGate.CreatedAt, rasGate.UpdatedAt);
    }

    [Fact]
    public async Task Update_preserves_created_at_and_refreshes_updated_at()
    {
        await using var db = _database.CreateContext();
        var rasGate = RasGateTestData.Create();
        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var createdAt = rasGate.CreatedAt;

        rasGate.Name = "Updated gate";
        rasGate.UpdatedAt = DateTime.UnixEpoch;
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(createdAt, rasGate.CreatedAt);
        Assert.Equal(
            InitialTime.AddMinutes(1).UtcDateTime,
            rasGate.UpdatedAt);
    }

    [Fact]
    public async Task Delete_marks_entity_as_deleted_and_hides_it_from_regular_queries()
    {
        var rasGate = RasGateTestData.Create();

        await using (var db = _database.CreateContext())
        {
            db.RasGates.Add(rasGate);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.RasGates.Remove(rasGate);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verificationDb = _database.CreateContext();
        Assert.Null(await verificationDb.RasGates.SingleOrDefaultAsync(
            x => x.Id == rasGate.Id,
            TestContext.Current.CancellationToken));

        var deleted = await verificationDb.RasGates
            .IgnoreQueryFilters()
            .SingleAsync(
                x => x.Id == rasGate.Id,
                TestContext.Current.CancellationToken);

        Assert.True(deleted.IsDeleted);
        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal(deleted.DeletedAt, deleted.UpdatedAt);
    }

    [Fact]
    public async Task Restore_clears_deletion_state_and_returns_entity_to_regular_queries()
    {
        var rasGate = RasGateTestData.Create();

        await using (var db = _database.CreateContext())
        {
            db.RasGates.Add(rasGate);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.RasGates.Remove(rasGate);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        await using (var restoreDb = _database.CreateContext())
        {
            var deleted = await restoreDb.RasGates
                .IgnoreQueryFilters()
                .SingleAsync(
                    item => item.Id == rasGate.Id,
                    TestContext.Current.CancellationToken);

            deleted.IsDeleted = false;
            deleted.DeletedAt = null;
            await restoreDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verificationDb = _database.CreateContext();
        var restored = await verificationDb.RasGates.SingleAsync(
            item => item.Id == rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeletedAt);
        Assert.Equal(
            InitialTime.AddMinutes(1).UtcDateTime,
            restored.UpdatedAt);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
