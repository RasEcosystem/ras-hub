using System.Net;
using Microsoft.AspNetCore.Builder;
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
using RasHub.Application.RasEndpoints.Models;
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
    private readonly IPAddress? _remoteIpAddress;
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
        IReadOnlyDictionary<string, string?>? settings = null,
        IPAddress? remoteIpAddress = null)
    {
        _environment = environment;
        _remoteIpAddress = remoteIpAddress;
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
        return CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    }

    public async Task SeedIdentityUserAsync(string email, string password)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var result = await userManager.CreateAsync(
            new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true },
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

    public async Task<RasEndpoint> SeedRasEndpointAsync(
        Guid rasGateId,
        string name = "Production RAS",
        string host = "ras.example.test",
        int port = 1545,
        bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var endpoint = new RasEndpoint
        {
            Name = name,
            RasGateId = rasGateId,
            Host = host,
            Port = port,
            IsActive = isActive
        };

        db.RasEndpoints.Add(endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return endpoint;
    }

    public async Task<RasEndpoint> SeedRasEndpointForGateAsync(
        RasGate rasGate,
        string name = "Production RAS",
        string host = "ras.example.test",
        int port = 1545,
        bool isActive = true)
    {
        var endpoint = await SeedRasEndpointAsync(
            rasGate.Id,
            name,
            host,
            port,
            isActive);
        return endpoint;
    }

    public async Task<RasEndpoint?> FindRasEndpointAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();

        return await db.RasEndpoints
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                endpoint => endpoint.Id == id,
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
        Guid rasEndpointId,
        bool includeDeleted = false)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var query = includeDeleted
            ? db.RasClusters.IgnoreQueryFilters()
            : db.RasClusters;

        return await query
            .AsNoTracking()
            .Where(cluster => cluster.RasEndpointId == rasEndpointId)
            .OrderBy(cluster => cluster.ExternalId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task<RasCluster> SeedRasClusterAsync(
        Guid rasEndpointId,
        Guid? externalId = null,
        string name = "Main cluster",
        string host = "cluster.example.test")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var cluster = new RasCluster
        {
            RasEndpointId = rasEndpointId,
            ExternalId = externalId ?? Guid.NewGuid(),
            Name = name,
            Host = host,
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

    public async Task<RasInfobase> SeedRasInfobaseAsync(
        Guid rasClusterId,
        Guid? externalId = null,
        string name = "Main infobase",
        string description = "")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var infobase = new RasInfobase
        {
            RasClusterId = rasClusterId,
            ExternalId = externalId ?? Guid.NewGuid(),
            Name = name,
            Description = description,
            ObservedAt = DateTime.UtcNow
        };

        db.RasInfobases.Add(infobase);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return infobase;
    }

    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        db.RasInfobases.IgnoreQueryFilters().ExecuteDelete();
        db.RasClusters.IgnoreQueryFilters().ExecuteDelete();
        db.RasEndpoints.IgnoreQueryFilters().ExecuteDelete();
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
            if (_remoteIpAddress is not null)
                services.AddSingleton<IStartupFilter>(
                    new RemoteIpAddressStartupFilter(_remoteIpAddress));

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
                        RasEndpointConfigurationRevisionInterceptor>(),
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
            identityDb.UserRoles.Add(new IdentityUserRole<string> { UserId = "api-user", RoleId = adminRoleId });
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

    private sealed class RemoteIpAddressStartupFilter(IPAddress remoteIpAddress)
        : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return application =>
            {
                application.Use(async (context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = remoteIpAddress;
                    await nextMiddleware(context);
                });
                next(application);
            };
        }
    }
}

public sealed class FakeRasGateBoundary
{
    private int _clusterCreateRequestCount;
    private int _clusterInfoRequestCount;
    private TaskCompletionSource<bool>? _clusterPublicationRelease;
    private TaskCompletionSource<bool>? _clusterPublicationStarted;
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

    public Guid? LastRasEndpointId { get; private set; }

    public Guid? LastRasGateId { get; private set; }

    public string? LastRasEndpointAddress { get; private set; }

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

    public Exception? StatusException { get; set; }

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

    public void PauseClusterPublications()
    {
        _clusterPublicationStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _clusterPublicationRelease = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task WaitForClusterPublicationAsync(
        CancellationToken cancellationToken)
    {
        return (_clusterPublicationStarted?.Task ??
                throw new InvalidOperationException(
                    "Cluster publications are not paused."))
            .WaitAsync(cancellationToken);
    }

    public void ReleaseClusterPublications()
    {
        _clusterPublicationRelease?.TrySetResult(true);
    }

    public void Reset()
    {
        ReleaseClusterPublications();
        ReleaseStatusRequests();
        _clusterPublicationStarted = null;
        _clusterPublicationRelease = null;
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
        LastRasEndpointId = null;
        LastRasGateId = null;
        LastRasEndpointAddress = null;
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
        StatusException = null;
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

    private async Task WaitBeforeClusterPublicationAsync(
        CancellationToken cancellationToken)
    {
        _clusterPublicationStarted?.TrySetResult(true);

        if (_clusterPublicationRelease is not null)
            await _clusterPublicationRelease.Task.WaitAsync(cancellationToken);
    }

    private IReadOnlyList<RasResourceCapability> CreateCapabilities()
    {
        var capabilities = new List<RasResourceCapability>();

        AddCapability(SupportsClusterSnapshots, "clusters", "snapshot");
        AddCapability(SupportsClusterInfo, "clusters", "info");
        AddCapability(SupportsClusterRemove, "clusters", "remove");
        AddCapability(SupportsClusterInsert, "clusters", "insert");
        AddCapability(SupportsClusterUpdate, "clusters", "update");
        AddCapability(SupportsInfobaseSnapshots, "infobases", "snapshot");
        AddCapability(SupportsInfobaseInfo, "infobases", "info");

        return capabilities;

        void AddCapability(bool supported, string resource, string operation)
        {
            if (supported)
                capabilities.Add(new RasResourceCapability(
                    resource,
                    operation,
                    1));
        }
    }

    private void CaptureTarget(RasEndpointExecutionTarget target)
    {
        LastApiKey = target.Gate.ApiKey;
        LastRasEndpointId = target.Endpoint.Id;
        LastRasGateId = target.Gate.Id;
        LastRasEndpointAddress = target.Address.ToString();
    }

    private sealed class FakeRasGateStatusGateway(
        FakeRasGateBoundary owner)
        : IRasGateStatusGateway
    {
        public async Task<RasGateStatus> GetStatusAsync(
            RasGate rasGate,
            CancellationToken cancellationToken)
        {
            owner.LastApiKey = rasGate.ApiKey;
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._statusRequestCount);
            owner._statusRequestStarted?.TrySetResult(true);

            if (owner._statusRequestRelease is not null)
                await owner._statusRequestRelease.Task.WaitAsync(
                    cancellationToken);

            if (owner.StatusException is not null)
                throw owner.StatusException;

            return owner.Status;
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
            owner.LastApiKey = rasGate.ApiKey;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new RasGateCapabilities
            {
                RacVersion = "8.3.27.2214",
                Resources = owner.CreateCapabilities()
            });
        }

        public Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
            RasEndpointExecutionTarget target,
            CancellationToken cancellationToken)
        {
            owner.CaptureTarget(target);
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

        public async Task<RasClusterSnapshot> GetClusterAsync(
            RasEndpointExecutionTarget target,
            Guid clusterId,
            CancellationToken cancellationToken)
        {
            owner.CaptureTarget(target);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterInfoRequestCount);

            if (owner.ClusterException is not null)
                throw owner.ClusterException;

            var cluster = owner.Cluster ?? owner.Clusters.SingleOrDefault(item => item.ExternalId == clusterId);

            if (cluster is null)
                throw new RasGateClientException(
                    $"Cluster '{clusterId}' is unavailable.");

            await owner.WaitBeforeClusterPublicationAsync(cancellationToken);
            return cluster;
        }

        public Task<Guid> CreateClusterAsync(
            RasEndpointExecutionTarget target,
            RasClusterCreationOptions options,
            CancellationToken cancellationToken)
        {
            owner.CaptureTarget(target);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterCreateRequestCount);

            if (owner.ClusterCreateException is not null)
                throw owner.ClusterCreateException;

            owner.LastClusterCreationOptions = options;
            return Task.FromResult(owner.CreatedClusterId);
        }

        public Task UpdateClusterAsync(
            RasEndpointExecutionTarget target,
            Guid clusterId,
            RasClusterUpdateOptions options,
            CancellationToken cancellationToken)
        {
            owner.CaptureTarget(target);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterUpdateRequestCount);

            if (owner.ClusterUpdateException is not null)
                throw owner.ClusterUpdateException;

            owner.UpdatedClusterId = clusterId;
            owner.LastClusterUpdateOptions = options;
            return Task.CompletedTask;
        }

        public async Task RemoveClusterAsync(
            RasEndpointExecutionTarget target,
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            owner.CaptureTarget(target);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._clusterRemoveRequestCount);

            if (owner.ClusterRemoveException is not null)
                throw owner.ClusterRemoveException;

            owner.RemovedClusterId = clusterId;
            owner.LastClusterUser = clusterUser;
            owner.LastClusterPassword = clusterPassword;
            await owner.WaitBeforeClusterPublicationAsync(cancellationToken);
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
            owner.LastApiKey = rasGate.ApiKey;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new RasGateCapabilities
            {
                RacVersion = "8.3.27.2214",
                Resources = owner.CreateCapabilities()
            });
        }

        public Task<RasResourceSnapshot<RasInfobaseSnapshot>> GetInfobasesAsync(
            RasEndpointExecutionTarget target,
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            owner.CaptureTarget(target);
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
            RasEndpointExecutionTarget target,
            Guid clusterId,
            Guid infobaseId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            owner.CaptureTarget(target);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref owner._infobaseInfoRequestCount);

            if (owner.InfobaseException is not null)
                throw owner.InfobaseException;

            owner.RequestedInfobaseClusterId = clusterId;
            owner.RequestedInfobaseId = infobaseId;
            owner.LastClusterUser = clusterUser;
            owner.LastClusterPassword = clusterPassword;
            var infobase = owner.Infobase ?? owner.Infobases.SingleOrDefault(item => item.ExternalId == infobaseId);

            return Task.FromResult(infobase ?? throw new RasGateClientException(
                $"Infobase '{infobaseId}' is unavailable."));
        }
    }
}
