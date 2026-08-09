using RasHub.Contracts.Common.Pagination;
using RasHub.Domain;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Infrastructure.IntegrationTests.Database;

namespace RasHub.Infrastructure.IntegrationTests.Queries;

public sealed class RasGateQueriesTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Get_by_id_projects_public_fields_and_returns_no_tracked_entity()
    {
        var rasGate = RasGateTestData.Create(
            "Main gate",
            "https://main.example.test",
            8443,
            "must-not-be-projected");

        await using var db = _database.CreateContext();
        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var result = await new RasGateQueries(db).GetByIdAsync(
            rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(rasGate.Id, result.Id);
        Assert.Equal(rasGate.Name, result.Name);
        Assert.Equal(rasGate.Url, result.Url);
        Assert.Equal(rasGate.Port, result.Port);
        Assert.True(result.IsActive);
        Assert.Equal(rasGate.CreatedAt, result.CreatedAt);
        Assert.Equal(rasGate.UpdatedAt, result.UpdatedAt);
        Assert.Empty(db.ChangeTracker.Entries<RasGate>());
    }

    [Fact]
    public async Task Get_by_id_returns_null_for_missing_and_soft_deleted_entities()
    {
        var deleted = RasGateTestData.Create("Deleted");

        await using var db = _database.CreateContext();
        db.RasGates.Add(deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.RasGates.Remove(deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var queries = new RasGateQueries(db);

        Assert.Null(await queries.GetByIdAsync(
            deleted.Id,
            TestContext.Current.CancellationToken));
        Assert.Null(await queries.GetByIdAsync(
            Guid.NewGuid(),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_paged_returns_count_and_stable_order_for_the_requested_page()
    {
        var oldest = RasGateTestData.Create("Oldest");
        var recentA = RasGateTestData.Create("Recent A");
        var recentB = RasGateTestData.Create("Recent B");
        var deleted = RasGateTestData.Create("Deleted");
        var recentAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        await using var db = _database.CreateContext();
        db.RasGates.AddRange(oldest, recentA, recentB, deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        oldest.CreatedAt = recentAt.AddDays(-1);
        recentA.CreatedAt = recentAt;
        recentB.CreatedAt = recentAt;
        db.RasGates.Remove(deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var expectedIds = new[] { recentA.Id, recentB.Id }
            .OrderBy(id => id)
            .ToArray();

        var result = await new RasGateQueries(db).GetPagedAsync(
            new PageRequest(1, 2),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(expectedIds, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Get_health_summary_counts_only_recently_seen_gates()
    {
        var now = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var online = RasGateTestData.Create("Online");
        var stale = RasGateTestData.Create("Stale");
        var neverSeen = RasGateTestData.Create("Never seen");
        var inactive = RasGateTestData.Create("Inactive");
        var deleted = RasGateTestData.Create("Deleted");
        online.LastSeenAt = now.AddMinutes(-1);
        stale.LastSeenAt = now.AddMinutes(-10);
        inactive.IsActive = false;
        inactive.LastSeenAt = now;
        deleted.LastSeenAt = now;

        await using var db = _database.CreateContext();
        db.RasGates.AddRange(online, stale, neverSeen, inactive, deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.RasGates.Remove(deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new RasGateQueries(db).GetHealthSummaryAsync(
            now.AddMinutes(-3),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.OnlineCount);
    }

    [Fact]
    public async Task Get_active_ids_excludes_inactive_and_deleted_gates()
    {
        var active = RasGateTestData.Create("Active");
        var inactive = RasGateTestData.Create("Inactive");
        var deleted = RasGateTestData.Create("Deleted");
        inactive.IsActive = false;

        await using var db = _database.CreateContext();
        db.RasGates.AddRange(active, inactive, deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.RasGates.Remove(deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ids = await new RasGateQueries(db).GetActiveIdsAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal([active.Id], ids);
    }
}