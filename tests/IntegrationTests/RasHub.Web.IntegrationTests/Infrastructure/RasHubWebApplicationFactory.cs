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
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Models;
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

    public FakeRasGateClientFactory RasGateClientFactory { get; } = new();

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

    public async Task SetIdentityUserBlockedAsync(string email, bool isBlocked)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email) ??
                   throw new InvalidOperationException($"User '{email}' does not exist.");

        user.IsBlocked = isBlocked;
        var result = await userManager.UpdateSecurityStampAsync(user);

        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(
                "; ",
                result.Errors.Select(error => error.Description)));
    }

    public async Task<RasGate> SeedRasGateAsync(
        string name = "Gate",
        string url = "https://gate.example.test",
        int port = 443,
        string apiKey = "stored-secret",
        string? instanceName = null,
        string? version = null,
        DateTime? statusObservedAt = null,
        bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var rasGate = new RasGate
        {
            Name = name,
            Url = url,
            Port = port,
            ApiKey = apiKey,
            IsActive = isActive,
            InstanceName = instanceName,
            Version = version,
            StatusObservedAt = statusObservedAt
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

    public async Task<IReadOnlyList<RasCluster>> FindRasClustersAsync(
        Guid rasGateId,
        bool includeDeleted = false)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var query = includeDeleted
            ? db.RasClusters.IgnoreQueryFilters()
            : db.RasClusters;

        return await query
            .AsNoTracking()
            .Where(cluster => cluster.RasGateId == rasGateId)
            .OrderBy(cluster => cluster.ExternalId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        db.RasClusters.IgnoreQueryFilters().ExecuteDelete();
        db.RasGates.IgnoreQueryFilters().ExecuteDelete();
        RasGateClientFactory.Reset();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("ConnectionStrings:RasHub", "Host=unused");
        builder.UseSetting("Database:ApplyMigrations", "false");
        builder.UseSetting("Settings:InitializeOnStart", "false");
        builder.UseSetting("RasGateMonitoring:RunOnStartup", "false");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RasHub"] = "Host=unused",
                ["Database:ApplyMigrations"] = "false",
                ["Settings:InitializeOnStart"] = "false",
                ["RasGateMonitoring:RunOnStartup"] = "false"
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
            services.RemoveAll<IRasGateClientFactory>();

            services.AddDbContext<RasHubDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(
                    serviceProvider.GetRequiredService<AuditSoftDeleteInterceptor>());
            });

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(_identityConnection));

            services.AddSingleton<IRasGateClientFactory>(RasGateClientFactory);
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

public sealed class FakeRasGateClientFactory : IRasGateClientFactory
{
    private int _clusterRequestCount;
    private int _statusRequestCount;

    public int ClusterRequestCount => Volatile.Read(ref _clusterRequestCount);

    public int StatusRequestCount => Volatile.Read(ref _statusRequestCount);

    public IReadOnlyList<RasClusterSnapshot> Clusters { get; set; } = [];

    public Exception? ClustersException { get; set; }

    public RasGateStatus Status { get; set; } =
        new("Test RasGate", "1.0.0");

    public IRasGateClient Create(RasGate rasGate)
    {
        return new FakeRasGateClient(this);
    }

    public void Reset()
    {
        Volatile.Write(ref _clusterRequestCount, 0);
        Volatile.Write(ref _statusRequestCount, 0);
        Clusters = [];
        ClustersException = null;
        Status = new RasGateStatus("Test RasGate", "1.0.0");
    }

    private sealed class FakeRasGateClient(FakeRasGateClientFactory owner)
        : IRasGateClient
    {
        public Task<RasGateStatus> GetStatusAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._statusRequestCount);
            return Task.FromResult(owner.Status);
        }

        public Task<IReadOnlyList<RasClusterSnapshot>> GetClustersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterRequestCount);

            if (owner.ClustersException is not null)
                throw owner.ClustersException;

            return Task.FromResult(owner.Clusters);
        }
    }
}