using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
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
    public async Task Get_activity_returns_active_state_and_excludes_deleted_gate()
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
        db.ChangeTracker.Clear();
        var queries = new RasGateQueries(db);

        var activeResult = await queries.GetActivityAsync(
            active.Id,
            TestContext.Current.CancellationToken);
        var inactiveResult = await queries.GetActivityAsync(
            inactive.Id,
            TestContext.Current.CancellationToken);
        var deletedResult = await queries.GetActivityAsync(
            deleted.Id,
            TestContext.Current.CancellationToken);

        Assert.True(activeResult?.IsActive);
        Assert.False(inactiveResult?.IsActive);
        Assert.Null(deletedResult);
        Assert.Empty(db.ChangeTracker.Entries<RasGate>());
    }

    [Fact]
    public async Task Get_administration_items_projects_operational_state_without_secrets()
    {
        var observedAt = new DateTime(2026, 8, 11, 10, 0, 0, DateTimeKind.Utc);
        var lastSeenAt = observedAt.AddMinutes(1);
        var gate = RasGateTestData.Create(
            "Main gate",
            "https://main.example.test",
            8443,
            "must-not-be-projected");
        var deleted = RasGateTestData.Create("Deleted gate");
        gate.InstanceName = "rasgate-main";
        gate.Version = "1.2.3";
        gate.StatusObservedAt = observedAt;
        gate.RacAvailable = true;
        gate.RacVersion = "8.3.27.2214";
        gate.RacStatusObservedAt = observedAt;
        gate.LastSeenAt = lastSeenAt;

        await using var db = _database.CreateContext();
        db.RasGates.AddRange(gate, deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.RasGates.Remove(deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var result = await new RasGateQueries(db).GetAdministrationItemsAsync(
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result);
        Assert.Equal(gate.Id, item.Id);
        Assert.Equal("Main gate", item.Name);
        Assert.Equal("https://main.example.test", item.Url);
        Assert.Equal(8443, item.Port);
        Assert.Equal("rasgate-main", item.InstanceName);
        Assert.Equal("1.2.3", item.Version);
        Assert.Equal(observedAt, item.StatusObservedAt);
        Assert.True(item.RacAvailable);
        Assert.Equal("8.3.27.2214", item.RacVersion);
        Assert.Equal(observedAt, item.RacStatusObservedAt);
        Assert.Equal(lastSeenAt, item.LastSeenAt);
        Assert.Equal(
            RasGateHealthState.Ready,
            item.GetHealthState(observedAt.AddMinutes(-1)));
        Assert.False(item.IsDeleted);
        Assert.Null(item.DeletedAt);
        Assert.Empty(db.ChangeTracker.Entries<RasGate>());

        var itemsIncludingDeleted = await new RasGateQueries(db)
            .GetAdministrationItemsAsync(
                true,
                TestContext.Current.CancellationToken);

        Assert.Equal(2, itemsIncludingDeleted.Count);
        var deletedItem = Assert.Single(itemsIncludingDeleted,
            candidate =>
                candidate.Id == deleted.Id);
        Assert.True(deletedItem.IsDeleted);
        Assert.NotNull(deletedItem.DeletedAt);
    }

    [Fact]
    public void Administration_item_health_uses_aggregate_status_observations()
    {
        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var onlineSince = now.AddMinutes(-3);

        Assert.Equal(
            RasGateHealthState.Unknown,
            CreateAdministrationItem(null, true, now, now)
                .GetHealthState(onlineSince));
        Assert.Equal(
            RasGateHealthState.Offline,
            CreateAdministrationItem(now.AddMinutes(-10), true, now, now)
                .GetHealthState(onlineSince));
        Assert.Equal(
            RasGateHealthState.Degraded,
            CreateAdministrationItem(now, false, now, now)
                .GetHealthState(onlineSince));
        Assert.Equal(
            RasGateHealthState.Ready,
            CreateAdministrationItem(now, true, now, now)
                .GetHealthState(onlineSince));
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
    public async Task Get_health_summary_counts_only_fresh_gate_status_observations()
    {
        var now = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var online = RasGateTestData.Create("Online");
        var stale = RasGateTestData.Create("Stale");
        var neverSeen = RasGateTestData.Create("Never seen");
        var inactive = RasGateTestData.Create("Inactive");
        var deleted = RasGateTestData.Create("Deleted");
        online.StatusObservedAt = now.AddMinutes(-1);
        online.RacAvailable = false;
        stale.StatusObservedAt = now.AddMinutes(-10);
        stale.LastSeenAt = now;
        neverSeen.LastSeenAt = now;
        inactive.IsActive = false;
        inactive.StatusObservedAt = now;
        deleted.StatusObservedAt = now;

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
    public async Task Get_health_summary_without_active_gates_returns_zero_counts()
    {
        await using var db = _database.CreateContext();

        var result = await new RasGateQueries(db).GetHealthSummaryAsync(
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.OnlineCount);
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

    private static RasGateAdministrationItem CreateAdministrationItem(
        DateTime? rasGateObservedAt,
        bool? racAvailable,
        DateTime? racObservedAt,
        DateTime? lastSeenAt)
    {
        var changedAt = new DateTime(
            2026,
            8,
            20,
            12,
            0,
            0,
            DateTimeKind.Utc);

        return new RasGateAdministrationItem
        {
            Id = Guid.NewGuid(),
            Name = "Gate",
            Url = "https://gate.example.test",
            Port = 443,
            IsActive = true,
            ConfigurationRevision = 1,
            InstanceName = "Remote Gate",
            Version = "1.2.3",
            StatusObservedAt = rasGateObservedAt,
            RacAvailable = racAvailable,
            RacVersion = racAvailable == true ? "8.3.27.2214" : null,
            RacStatusObservedAt = racObservedAt,
            LastSeenAt = lastSeenAt,
            CreatedAt = changedAt,
            UpdatedAt = changedAt,
            IsDeleted = false,
            DeletedAt = null
        };
    }
}
