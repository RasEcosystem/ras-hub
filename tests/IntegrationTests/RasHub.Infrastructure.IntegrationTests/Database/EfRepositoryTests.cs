using RasHub.Domain;
using RasHub.Infrastructure.Database;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class EfRepositoryTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task Add_and_get_by_id_round_trip_entity()
    {
        var rasGate = RasGateTestData.Create();

        await using (var writeDb = _database.CreateContext())
        {
            var repository = new EfRepository<RasGate>(writeDb);
            await repository.AddAsync(rasGate, TestContext.Current.CancellationToken);
            await writeDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readDb = _database.CreateContext();
        var readRepository = new EfRepository<RasGate>(readDb);
        var result = await readRepository.GetByIdAsync(
            rasGate.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(rasGate.Id, result.Id);
        Assert.Equal(rasGate.ApiKey, result.ApiKey);
    }

    [Fact]
    public async Task Get_by_ids_ignores_duplicates_and_unknown_ids()
    {
        var first = RasGateTestData.Create("First");
        var second = RasGateTestData.Create("Second");

        await using var db = _database.CreateContext();
        db.RasGates.AddRange(first, second);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new EfRepository<RasGate>(db);

        var result = await repository.GetByIdsAsync(
            [first.Id, first.Id, Guid.NewGuid()],
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result);
        Assert.Equal(first.Id, item.Id);
    }

    [Fact]
    public async Task List_applies_the_requested_predicate()
    {
        var matching = RasGateTestData.Create("Matching", port: 443);
        var other = RasGateTestData.Create("Other", port: 80);

        await using var db = _database.CreateContext();
        db.RasGates.AddRange(matching, other);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new EfRepository<RasGate>(db);

        var result = await repository.ListAsync(
            x => x.Port == 443,
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result);
        Assert.Equal(matching.Id, item.Id);
    }
}