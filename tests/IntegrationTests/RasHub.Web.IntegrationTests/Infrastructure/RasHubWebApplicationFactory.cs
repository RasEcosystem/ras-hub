using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RasHub.Domain;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Interceptors;
using RasHub.Web.Authentication;

namespace RasHub.Web.IntegrationTests.Infrastructure;

public sealed class RasHubWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "integration-test-api-key";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public RasHubWebApplicationFactory()
    {
        _connection.Open();
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, ApiKey);
        return client;
    }

    public async Task<RasGate> SeedRasGateAsync(
        string name = "Gate",
        string url = "https://gate.example.test",
        int port = 443,
        string apiKey = "stored-secret")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var rasGate = new RasGate
        {
            Name = name,
            Url = url,
            Port = port,
            ApiKey = apiKey
        };

        db.RasGates.Add(rasGate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return rasGate;
    }

    public async Task<RasGate?> FindRasGateAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();

        return await db.RasGates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                rasGate => rasGate.Id == id,
                TestContext.Current.CancellationToken);
    }

    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        db.RasGates.IgnoreQueryFilters().ExecuteDelete();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:RasHub", "Host=unused");
        builder.UseSetting("Database:ApplyMigrations", "false");
        builder.UseSetting("RasHub:ApiKey", ApiKey);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RasHub"] = "Host=unused",
                ["Database:ApplyMigrations"] = "false",
                ["RasHub:ApiKey"] = ApiKey
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<RasHubDbContext>();
            services.RemoveAll<DbContextOptions<RasHubDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<RasHubDbContext>>();

            services.AddDbContext<RasHubDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(
                    serviceProvider.GetRequiredService<AuditSoftDeleteInterceptor>());
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        db.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _connection.Dispose();
    }
}