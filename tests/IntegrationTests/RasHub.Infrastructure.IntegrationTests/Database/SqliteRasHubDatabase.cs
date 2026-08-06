using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Interceptors;

namespace RasHub.Infrastructure.IntegrationTests.Database;

internal sealed class SqliteRasHubDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AuditSoftDeleteInterceptor _interceptor = new();

    public SqliteRasHubDatabase()
    {
        _connection.Open();

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    public RasHubDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RasHubDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_interceptor)
            .Options;

        return new RasHubDbContext(options);
    }
}