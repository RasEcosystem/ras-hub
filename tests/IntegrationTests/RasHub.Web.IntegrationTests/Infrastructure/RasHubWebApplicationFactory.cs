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

    public FakeRasGateBoundary RasGateBoundary { get; } = new();

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

    public async Task<RasCluster> SeedRasClusterAsync(
        Guid rasGateId,
        Guid? externalId = null,
        string name = "Main cluster")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var cluster = new RasCluster
        {
            RasGateId = rasGateId,
            ExternalId = externalId ?? Guid.NewGuid(),
            Name = name,
            Host = "cluster.example.test",
            Port = 1541,
            ObservedAt = DateTime.UtcNow
        };

        db.RasClusters.Add(cluster);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return cluster;
    }

    public async Task<IReadOnlyList<RasInfobase>> FindRasInfobasesAsync(
        Guid rasClusterId,
        bool includeDeleted = false)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var query = includeDeleted
            ? db.RasInfobases.IgnoreQueryFilters()
            : db.RasInfobases;

        return await query
            .AsNoTracking()
            .Where(infobase => infobase.RasClusterId == rasClusterId)
            .OrderBy(infobase => infobase.ExternalId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        db.RasInfobases.IgnoreQueryFilters().ExecuteDelete();
        db.RasClusters.IgnoreQueryFilters().ExecuteDelete();
        db.RasGates.IgnoreQueryFilters().ExecuteDelete();
        RasGateBoundary.Reset();
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
            services.RemoveAll<IRasGateStatusGateway>();
            services.RemoveAll<IRasClusterGateway>();
            services.RemoveAll<IRasInfobaseGateway>();

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

            services.AddSingleton(RasGateBoundary.StatusGateway);
            services.AddSingleton(RasGateBoundary.ClusterGateway);
            services.AddSingleton(RasGateBoundary.InfobaseGateway);
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

public sealed class FakeRasGateBoundary
{
    private int _clusterCreateRequestCount;
    private int _clusterInfoRequestCount;
    private int _clusterRemoveRequestCount;
    private int _clusterRequestCount;
    private int _clusterUpdateRequestCount;
    private int _infobaseInfoRequestCount;
    private int _infobaseRequestCount;
    private int _statusRequestCount;
    private TaskCompletionSource<bool>? _statusRequestRelease;
    private TaskCompletionSource<bool>? _statusRequestStarted;

    public FakeRasGateBoundary()
    {
        StatusGateway = new FakeRasGateStatusGateway(this);
        ClusterGateway = new FakeRasClusterGateway(this);
        InfobaseGateway = new FakeRasInfobaseGateway(this);
    }

    public IRasGateStatusGateway StatusGateway { get; }

    public IRasClusterGateway ClusterGateway { get; }

    public IRasInfobaseGateway InfobaseGateway { get; }

    public int ClusterRequestCount => Volatile.Read(ref _clusterRequestCount);

    public int ClusterInfoRequestCount => Volatile.Read(
        ref _clusterInfoRequestCount);

    public int ClusterRemoveRequestCount => Volatile.Read(
        ref _clusterRemoveRequestCount);

    public int ClusterCreateRequestCount => Volatile.Read(
        ref _clusterCreateRequestCount);

    public int ClusterUpdateRequestCount => Volatile.Read(
        ref _clusterUpdateRequestCount);

    public int InfobaseRequestCount => Volatile.Read(
        ref _infobaseRequestCount);

    public int InfobaseInfoRequestCount => Volatile.Read(
        ref _infobaseInfoRequestCount);

    public int StatusRequestCount => Volatile.Read(ref _statusRequestCount);

    public string? LastApiKey { get; private set; }

    public IReadOnlyList<RasClusterSnapshot> Clusters { get; set; } = [];

    public RasClusterSnapshot? Cluster { get; set; }

    public IReadOnlyList<RasInfobaseSnapshot> Infobases { get; set; } = [];

    public RasInfobaseSnapshot? Infobase { get; set; }

    public SnapshotCompleteness ClusterSnapshotCompleteness { get; set; } =
        SnapshotCompleteness.Complete;

    public SnapshotCompleteness InfobaseSnapshotCompleteness { get; set; } =
        SnapshotCompleteness.Complete;

    public bool SupportsClusterSnapshots { get; set; } = true;

    public bool SupportsClusterInfo { get; set; } = true;

    public bool SupportsClusterRemove { get; set; } = true;

    public bool SupportsClusterInsert { get; set; } = true;

    public bool SupportsClusterUpdate { get; set; } = true;

    public bool SupportsInfobaseSnapshots { get; set; } = true;

    public bool SupportsInfobaseInfo { get; set; } = true;

    public Exception? ClustersException { get; set; }

    public Exception? ClusterException { get; set; }

    public Exception? ClusterRemoveException { get; set; }

    public Exception? ClusterCreateException { get; set; }

    public Exception? ClusterUpdateException { get; set; }

    public Exception? InfobasesException { get; set; }

    public Exception? InfobaseException { get; set; }

    public Guid CreatedClusterId { get; set; } = Guid.NewGuid();

    public RasClusterCreationOptions? LastClusterCreationOptions { get; private set; }

    public Guid? UpdatedClusterId { get; private set; }

    public RasClusterUpdateOptions? LastClusterUpdateOptions { get; private set; }

    public Guid? RemovedClusterId { get; private set; }

    public string? LastClusterUser { get; private set; }

    public string? LastClusterPassword { get; private set; }

    public Guid? RequestedInfobaseClusterId { get; private set; }

    public Guid? RequestedInfobaseId { get; private set; }

    public RasGateStatus Status { get; set; } =
        new("Test RasGate", "1.0.0");

    private FakeRasGateClient Create(RasGate rasGate)
    {
        LastApiKey = rasGate.ApiKey;
        return new FakeRasGateClient(this);
    }

    public async Task<RasGateStatus> GetStatusAsync(
        RasGate rasGate,
        CancellationToken cancellationToken)
    {
        LastApiKey = rasGate.ApiKey;
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _statusRequestCount);
        _statusRequestStarted?.TrySetResult(true);

        if (_statusRequestRelease is not null)
            await _statusRequestRelease.Task.WaitAsync(cancellationToken);

        return Status;
    }

    public Task<RasGateCapabilities> GetCapabilitiesAsync(
        RasGate rasGate,
        CancellationToken cancellationToken)
    {
        return Create(rasGate).GetCapabilitiesAsync(cancellationToken);
    }

    public Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
        RasGate rasGate,
        CancellationToken cancellationToken)
    {
        return Create(rasGate).GetClustersAsync(cancellationToken);
    }

    public Task<RasClusterSnapshot> GetClusterAsync(
        RasGate rasGate,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        return Create(rasGate).GetClusterAsync(clusterId, cancellationToken);
    }

    public Task<Guid> CreateClusterAsync(
        RasGate rasGate,
        RasClusterCreationOptions options,
        CancellationToken cancellationToken)
    {
        return Create(rasGate).CreateClusterAsync(options, cancellationToken);
    }

    public Task UpdateClusterAsync(
        RasGate rasGate,
        Guid clusterId,
        RasClusterUpdateOptions options,
        CancellationToken cancellationToken)
    {
        return Create(rasGate).UpdateClusterAsync(
            clusterId,
            options,
            cancellationToken);
    }

    public Task RemoveClusterAsync(
        RasGate rasGate,
        Guid clusterId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken)
    {
        return Create(rasGate).RemoveClusterAsync(
            clusterId,
            clusterUser,
            clusterPassword,
            cancellationToken);
    }

    public Task<RasResourceSnapshot<RasInfobaseSnapshot>> GetInfobasesAsync(
        RasGate rasGate,
        Guid clusterId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken)
    {
        return Create(rasGate).GetInfobasesAsync(
            clusterId,
            clusterUser,
            clusterPassword,
            cancellationToken);
    }

    public Task<RasInfobaseSnapshot> GetInfobaseAsync(
        RasGate rasGate,
        Guid clusterId,
        Guid infobaseId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken)
    {
        return Create(rasGate).GetInfobaseAsync(
            clusterId,
            infobaseId,
            clusterUser,
            clusterPassword,
            cancellationToken);
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
        Volatile.Write(ref _clusterCreateRequestCount, 0);
        Volatile.Write(ref _clusterUpdateRequestCount, 0);
        Volatile.Write(ref _clusterRemoveRequestCount, 0);
        Volatile.Write(ref _clusterRequestCount, 0);
        Volatile.Write(ref _infobaseRequestCount, 0);
        Volatile.Write(ref _infobaseInfoRequestCount, 0);
        Volatile.Write(ref _statusRequestCount, 0);
        LastApiKey = null;
        Clusters = [];
        Cluster = null;
        Infobases = [];
        Infobase = null;
        ClusterSnapshotCompleteness = SnapshotCompleteness.Complete;
        InfobaseSnapshotCompleteness = SnapshotCompleteness.Complete;
        SupportsClusterSnapshots = true;
        SupportsClusterInfo = true;
        SupportsClusterRemove = true;
        SupportsClusterInsert = true;
        SupportsClusterUpdate = true;
        SupportsInfobaseSnapshots = true;
        SupportsInfobaseInfo = true;
        ClustersException = null;
        ClusterException = null;
        ClusterRemoveException = null;
        ClusterCreateException = null;
        ClusterUpdateException = null;
        InfobasesException = null;
        InfobaseException = null;
        CreatedClusterId = Guid.NewGuid();
        LastClusterCreationOptions = null;
        UpdatedClusterId = null;
        LastClusterUpdateOptions = null;
        RemovedClusterId = null;
        LastClusterUser = null;
        LastClusterPassword = null;
        RequestedInfobaseClusterId = null;
        RequestedInfobaseId = null;
        Status = new RasGateStatus("Test RasGate", "1.0.0");
    }

    private sealed class FakeRasGateClient(FakeRasGateBoundary owner)
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

        public Task<RasResourceSnapshot<RasInfobaseSnapshot>> GetInfobasesAsync(
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._infobaseRequestCount);

            if (owner.InfobasesException is not null)
                throw owner.InfobasesException;

            owner.RequestedInfobaseClusterId = clusterId;
            owner.LastClusterUser = clusterUser;
            owner.LastClusterPassword = clusterPassword;

            return Task.FromResult(
                new RasResourceSnapshot<RasInfobaseSnapshot>
                {
                    SchemaVersion = 1,
                    SourceVersion = "8.3.27.2214",
                    Completeness = owner.InfobaseSnapshotCompleteness,
                    Items = owner.Infobases
                });
        }

        public Task<RasInfobaseSnapshot> GetInfobaseAsync(
            Guid clusterId,
            Guid infobaseId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._infobaseInfoRequestCount);

            if (owner.InfobaseException is not null)
                throw owner.InfobaseException;

            owner.RequestedInfobaseClusterId = clusterId;
            owner.RequestedInfobaseId = infobaseId;
            owner.LastClusterUser = clusterUser;
            owner.LastClusterPassword = clusterPassword;
            var infobase = owner.Infobase ?? owner.Infobases
                .SingleOrDefault(item => item.ExternalId == infobaseId);

            return Task.FromResult(infobase ?? throw new RasGateClientException(
                $"Infobase '{infobaseId}' is unavailable."));
        }

        public Task RemoveClusterAsync(
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterRemoveRequestCount);

            if (owner.ClusterRemoveException is not null)
                throw owner.ClusterRemoveException;

            owner.RemovedClusterId = clusterId;
            owner.LastClusterUser = clusterUser;
            owner.LastClusterPassword = clusterPassword;
            return Task.CompletedTask;
        }

        public Task<Guid> CreateClusterAsync(
            RasClusterCreationOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterCreateRequestCount);

            if (owner.ClusterCreateException is not null)
                throw owner.ClusterCreateException;

            owner.LastClusterCreationOptions = options;
            return Task.FromResult(owner.CreatedClusterId);
        }

        public Task UpdateClusterAsync(
            Guid clusterId,
            RasClusterUpdateOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterUpdateRequestCount);

            if (owner.ClusterUpdateException is not null)
                throw owner.ClusterUpdateException;

            owner.UpdatedClusterId = clusterId;
            owner.LastClusterUpdateOptions = options;
            return Task.CompletedTask;
        }

        private static IReadOnlyList<RasResourceCapability> GetCapabilities(
            FakeRasGateBoundary owner)
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

            if (owner.SupportsClusterRemove)
                capabilities.Add(new RasResourceCapability(
                    "clusters",
                    "remove",
                    1));

            if (owner.SupportsClusterInsert)
                capabilities.Add(new RasResourceCapability(
                    "clusters",
                    "insert",
                    1));

            if (owner.SupportsClusterUpdate)
                capabilities.Add(new RasResourceCapability(
                    "clusters",
                    "update",
                    1));

            if (owner.SupportsInfobaseSnapshots)
                capabilities.Add(new RasResourceCapability(
                    "infobases",
                    "snapshot",
                    1));

            if (owner.SupportsInfobaseInfo)
                capabilities.Add(new RasResourceCapability(
                    "infobases",
                    "info",
                    1));

            return capabilities;
        }
    }

    private sealed class FakeRasGateStatusGateway(
        FakeRasGateBoundary owner)
        : IRasGateStatusGateway
    {
        public Task<RasGateStatus> GetStatusAsync(
            RasGate rasGate,
            CancellationToken cancellationToken)
        {
            return owner.GetStatusAsync(rasGate, cancellationToken);
        }
    }

    private sealed class FakeRasClusterGateway(
        FakeRasGateBoundary owner)
        : IRasClusterGateway
    {
        public Task<RasGateCapabilities> GetCapabilitiesAsync(
            RasGate rasGate,
            CancellationToken cancellationToken)
        {
            return owner.GetCapabilitiesAsync(rasGate, cancellationToken);
        }

        public Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
            RasGate rasGate,
            CancellationToken cancellationToken)
        {
            return owner.GetClustersAsync(rasGate, cancellationToken);
        }

        public Task<RasClusterSnapshot> GetClusterAsync(
            RasGate rasGate,
            Guid clusterId,
            CancellationToken cancellationToken)
        {
            return owner.GetClusterAsync(rasGate, clusterId, cancellationToken);
        }

        public Task<Guid> CreateClusterAsync(
            RasGate rasGate,
            RasClusterCreationOptions options,
            CancellationToken cancellationToken)
        {
            return owner.CreateClusterAsync(rasGate, options, cancellationToken);
        }

        public Task UpdateClusterAsync(
            RasGate rasGate,
            Guid clusterId,
            RasClusterUpdateOptions options,
            CancellationToken cancellationToken)
        {
            return owner.UpdateClusterAsync(
                rasGate,
                clusterId,
                options,
                cancellationToken);
        }

        public Task RemoveClusterAsync(
            RasGate rasGate,
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            return owner.RemoveClusterAsync(
                rasGate,
                clusterId,
                clusterUser,
                clusterPassword,
                cancellationToken);
        }
    }

    private sealed class FakeRasInfobaseGateway(
        FakeRasGateBoundary owner)
        : IRasInfobaseGateway
    {
        public Task<RasGateCapabilities> GetCapabilitiesAsync(
            RasGate rasGate,
            CancellationToken cancellationToken)
        {
            return owner.GetCapabilitiesAsync(rasGate, cancellationToken);
        }

        public Task<RasResourceSnapshot<RasInfobaseSnapshot>> GetInfobasesAsync(
            RasGate rasGate,
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            return owner.GetInfobasesAsync(
                rasGate,
                clusterId,
                clusterUser,
                clusterPassword,
                cancellationToken);
        }

        public Task<RasInfobaseSnapshot> GetInfobaseAsync(
            RasGate rasGate,
            Guid clusterId,
            Guid infobaseId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            return owner.GetInfobaseAsync(
                rasGate,
                clusterId,
                infobaseId,
                clusterUser,
                clusterPassword,
                cancellationToken);
        }
    }
}