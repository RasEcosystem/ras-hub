using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Interceptors;
using RasHub.Web.Authentication;
using RasHub.Web.Data;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.IntegrationTests.Infrastructure;

public sealed class RasHubWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "integration-test-api-key";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _environment;
    private readonly SqliteConnection _identityConnection = new("Data Source=:memory:");
    private readonly bool _seedApiUser;
    private readonly IReadOnlyDictionary<string, string?> _settings;

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
        bool seedApiUser = true,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        _environment = environment;
        _seedApiUser = seedApiUser;
        _settings = settings ?? new Dictionary<string, string?>();
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

    public HttpClient CreateIdentityClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
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

    public async Task<string?> FindStoredRasGateApiKeyAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();

        return await db.Database
            .SqlQuery<string>($"SELECT api_key AS \"Value\" FROM ras_gates WHERE id = {id}")
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
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
            configuration.AddInMemoryCollection(_settings);
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
                options.ConfigureWarnings(warnings => warnings.Ignore(
                    CoreEventId.ManyServiceProvidersCreatedWarning));
                options.AddInterceptors(
                    serviceProvider.GetRequiredService<AuditSoftDeleteInterceptor>(),
                    serviceProvider.GetRequiredService<
                        RasGateConfigurationRevisionInterceptor>(),
                    serviceProvider.GetRequiredService<
                        RasGateApiKeyProtectionInterceptor>());
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

            var adminRoleId = identityDb.Roles
                .Where(role => role.Name == AppRoles.Admin)
                .Select(role => role.Id)
                .Single();
            identityDb.UserRoles.Add(new IdentityUserRole<string>
            {
                UserId = "api-user",
                RoleId = adminRoleId
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
    private int _clusterInfoRequestCount;
    private int _clusterRequestCount;
    private int _statusRequestCount;
    private TaskCompletionSource<bool>? _statusRequestRelease;
    private TaskCompletionSource<bool>? _statusRequestStarted;

    public int ClusterRequestCount => Volatile.Read(ref _clusterRequestCount);

    public int ClusterInfoRequestCount => Volatile.Read(
        ref _clusterInfoRequestCount);

    public int StatusRequestCount => Volatile.Read(ref _statusRequestCount);

    public string? LastApiKey { get; private set; }

    public IReadOnlyList<RasClusterSnapshot> Clusters { get; set; } = [];

    public RasClusterSnapshot? Cluster { get; set; }

    public SnapshotCompleteness ClusterSnapshotCompleteness { get; set; } =
        SnapshotCompleteness.Complete;

    public bool SupportsClusterSnapshots { get; set; } = true;

    public bool SupportsClusterInfo { get; set; } = true;

    public Exception? ClustersException { get; set; }

    public Exception? ClusterException { get; set; }

    public RasGateStatus Status { get; set; } =
        new("Test RasGate", "1.0.0");

    public IRasGateClient Create(RasGate rasGate)
    {
        LastApiKey = rasGate.ApiKey;
        return new FakeRasGateClient(this);
    }

    public void PauseStatusRequests()
    {
        _statusRequestStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRequestRelease = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task WaitForStatusRequestAsync(CancellationToken cancellationToken)
    {
        return (_statusRequestStarted?.Task ??
                throw new InvalidOperationException(
                    "Status requests are not paused."))
            .WaitAsync(cancellationToken);
    }

    public void ReleaseStatusRequests()
    {
        _statusRequestRelease?.TrySetResult(true);
    }

    public void Reset()
    {
        ReleaseStatusRequests();
        _statusRequestStarted = null;
        _statusRequestRelease = null;
        Volatile.Write(ref _clusterInfoRequestCount, 0);
        Volatile.Write(ref _clusterRequestCount, 0);
        Volatile.Write(ref _statusRequestCount, 0);
        LastApiKey = null;
        Clusters = [];
        Cluster = null;
        ClusterSnapshotCompleteness = SnapshotCompleteness.Complete;
        SupportsClusterSnapshots = true;
        SupportsClusterInfo = true;
        ClustersException = null;
        ClusterException = null;
        Status = new RasGateStatus("Test RasGate", "1.0.0");
    }

    private sealed class FakeRasGateClient(FakeRasGateClientFactory owner)
        : IRasGateClient
    {
        public async Task<RasGateStatus> GetStatusAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._statusRequestCount);
            owner._statusRequestStarted?.TrySetResult(true);

            if (owner._statusRequestRelease is not null)
                await owner._statusRequestRelease.Task.WaitAsync(cancellationToken);

            return owner.Status;
        }

        public Task<RasGateCapabilities> GetCapabilitiesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new RasGateCapabilities
            {
                RacVersion = "8.3.27.2214",
                Resources = GetCapabilities(owner)
            });
        }

        public Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterRequestCount);

            if (owner.ClustersException is not null)
                throw owner.ClustersException;

            return Task.FromResult(
                new RasResourceSnapshot<RasClusterSnapshot>
                {
                    SchemaVersion = 1,
                    SourceVersion = "8.3.27.2214",
                    Completeness = owner.ClusterSnapshotCompleteness,
                    Items = owner.Clusters
                });
        }

        public Task<RasClusterSnapshot> GetClusterAsync(
            Guid clusterId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterInfoRequestCount);

            if (owner.ClusterException is not null)
                throw owner.ClusterException;

            var cluster = owner.Cluster ?? owner.Clusters.SingleOrDefault(item => item.ExternalId == clusterId);

            return Task.FromResult(cluster ?? throw new RasGateClientException(
                $"Cluster '{clusterId}' is unavailable."));
        }

        private static IReadOnlyList<RasResourceCapability> GetCapabilities(
            FakeRasGateClientFactory owner)
        {
            var capabilities = new List<RasResourceCapability>();

            if (owner.SupportsClusterSnapshots)
                capabilities.Add(new RasResourceCapability(
                    "clusters",
                    "snapshot",
                    1));

            if (owner.SupportsClusterInfo)
                capabilities.Add(new RasResourceCapability(
                    "clusters",
                    "info",
                    1));

            return capabilities;
        }
    }
}
