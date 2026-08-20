using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Interceptors;
using RasHub.Infrastructure.Database.Security;

namespace RasHub.Infrastructure.IntegrationTests.Database;

internal sealed class SqliteRasHubDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AuditSoftDeleteInterceptor _interceptor;
    private readonly RasGateApiKeyProtectionInterceptor _keyProtectionInterceptor;

    private readonly RasGateConfigurationRevisionInterceptor _revisionInterceptor =
        new();

    public SqliteRasHubDatabase(TimeProvider? timeProvider = null)
    {
        _interceptor = new AuditSoftDeleteInterceptor(
            timeProvider ?? TimeProvider.System);
        ApiKeyProtector = new RasGateApiKeyProtector(
            new EphemeralDataProtectionProvider());
        _keyProtectionInterceptor = new RasGateApiKeyProtectionInterceptor(
            ApiKeyProtector);
        _connection.Open();

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public RasGateApiKeyProtector ApiKeyProtector { get; }

    public void Dispose()
    {
        _connection.Dispose();
    }

    public RasHubDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RasHubDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(warnings => warnings.Ignore(
                CoreEventId.ManyServiceProvidersCreatedWarning))
            .AddInterceptors(
                _interceptor,
                _revisionInterceptor,
                _keyProtectionInterceptor)
            .Options;

        return new RasHubDbContext(options);
    }
}
