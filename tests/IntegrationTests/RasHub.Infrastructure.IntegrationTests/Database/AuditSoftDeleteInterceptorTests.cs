using Microsoft.EntityFrameworkCore;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class AuditSoftDeleteInterceptorTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Insert_sets_created_and_updated_timestamps()
    {
        await using var db = _database.CreateContext();
        var rasGate = RasGateTestData.Create();
        var beforeSave = DateTime.UtcNow;

        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var afterSave = DateTime.UtcNow;
        Assert.InRange(rasGate.CreatedAt, beforeSave, afterSave);
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
        var beforeSave = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(createdAt, rasGate.CreatedAt);
        Assert.InRange(rasGate.UpdatedAt, beforeSave, DateTime.UtcNow);
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
}