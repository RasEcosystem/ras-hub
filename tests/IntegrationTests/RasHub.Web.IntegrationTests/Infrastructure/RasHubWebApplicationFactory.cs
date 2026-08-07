using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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
using RasHub.Web.Data;

namespace RasHub.Web.IntegrationTests.Infrastructure;

public sealed class RasHubWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "integration-test-api-key";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _environment;
    private readonly SqliteConnection _identityConnection = new("Data Source=:memory:");
    private readonly bool _seedApiUser;

    public RasHubWebApplicationFactory()
        : this("Testing")
    {
    }

    internal RasHubWebApplicationFactory(bool seedApiUser)
        : this("Testing", seedApiUser)
    {
    }

    internal RasHubWebApplicationFactory(
        string environment,
        bool seedApiUser = true)
    {
        _environment = environment;
        _seedApiUser = seedApiUser;
        _connection.Open();
        _identityConnection.Open();
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, ApiKey);
        return client;
    }

    public async Task SeedIdentityUserAsync(string email, string password)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var result = await userManager.CreateAsync(
            new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            },
            password);

        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(
                "; ",
                result.Errors.Select(error => error.Description)));
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
        builder.UseEnvironment(_environment);
        builder.UseSetting("ConnectionStrings:RasHub", "Host=unused");
        builder.UseSetting("Database:ApplyMigrations", "false");
        builder.UseSetting("Settings:InitializeOnStart", "false");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RasHub"] = "Host=unused",
                ["Database:ApplyMigrations"] = "false",
                ["Settings:InitializeOnStart"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<RasHubDbContext>();
            services.RemoveAll<DbContextOptions<RasHubDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<RasHubDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();

            services.AddDbContext<RasHubDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(
                    serviceProvider.GetRequiredService<AuditSoftDeleteInterceptor>());
            });

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(_identityConnection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        db.Database.EnsureCreated();
        var identityDb = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();
        identityDb.Database.EnsureCreated();

        if (_seedApiUser &&
            !identityDb.Users.Any(user => user.ApiKey == ApiKey))
        {
            identityDb.Users.Add(new ApplicationUser
            {
                Id = "api-user",
                UserName = "api-user@example.test",
                NormalizedUserName = "API-USER@EXAMPLE.TEST",
                Email = "api-user@example.test",
                NormalizedEmail = "API-USER@EXAMPLE.TEST",
                EmailConfirmed = true,
                ApiKey = ApiKey,
                SecurityStamp = "api-user-security-stamp",
                ConcurrencyStamp = "api-user-concurrency-stamp"
            });
            identityDb.SaveChanges();
        }

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
            _identityConnection.Dispose();
        }
    }
}