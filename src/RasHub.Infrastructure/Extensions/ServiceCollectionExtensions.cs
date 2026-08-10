using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Interceptors;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Infrastructure.Database.Security;
using RasHub.Infrastructure.RasGates.Client;
using RasHub.Infrastructure.RasGates.Endpoints;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRasHubInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(RasHubDbContext.ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{RasHubDbContext.ConnectionStringName}' is required.");

        services.AddSingleton<AuditSoftDeleteInterceptor>();
        services.AddSingleton<RasGateConfigurationRevisionInterceptor>();
        services.AddSingleton<RasGateApiKeyProtector>();
        services.AddSingleton<RasGateApiKeyProtectionInterceptor>();

        services.AddDbContext<RasHubDbContext>((serviceProvider, options) =>
        {
            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditSoftDeleteInterceptor>(),
                serviceProvider.GetRequiredService<
                    RasGateConfigurationRevisionInterceptor>(),
                serviceProvider.GetRequiredService<
                    RasGateApiKeyProtectionInterceptor>());

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(RasHubDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure();
            });
        });

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IRasClusterSnapshotStore, RasClusterSnapshotStore>();
        services.AddScoped<IRasGateSyncPublisher, RasGateSyncPublisher>();
        services.AddScoped<RasClusterQueries>();
        services.AddScoped<RasGateQueries>();
        services.AddScoped<RasGateApiKeyProtectionMigrator>();

        services.AddSingleton<IRasGateEndpointFactory, RasGateEndpointFactory>();
        services.AddSingleton<RasGateHttpClientTransport>();
        services.AddSingleton<RacVersionParser>();
        services.AddSingleton<RacKeyValueOutputDeserializer>();
        services.AddSingleton<RacClusterOutputDeserializer>();
        services.AddSingleton<RacClusterSnapshotV1Adapter>();
        services.AddSingleton<RacClusterInfoV1Adapter>();
        services.AddSingleton<IRacResourceAdapter<RasClusterSnapshot>>(serviceProvider => serviceProvider
            .GetRequiredService<
                RacClusterSnapshotV1Adapter>());
        services.AddSingleton<IRacResourceAdapter<RasClusterSnapshot>>(serviceProvider => serviceProvider
            .GetRequiredService<
                RacClusterInfoV1Adapter>());
        services.AddSingleton<IRacResourceAdapterDescriptor>(serviceProvider => serviceProvider.GetRequiredService<
            RacClusterSnapshotV1Adapter>());
        services.AddSingleton<IRacResourceAdapterDescriptor>(serviceProvider => serviceProvider.GetRequiredService<
            RacClusterInfoV1Adapter>());
        services.AddSingleton<RacCapabilityResolver>();
        services.AddSingleton(typeof(RacResourceAdapterResolver<>));
        services.AddSingleton<IRasGateClientFactory, RasGateClientFactory>();

        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<RasHubDbContext>());

        return services;
    }

    public static async Task MigrateRasHubDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(ServiceCollectionExtensions));

        logger.LogInformation("Applying RasHub database migrations");
        await dbContext.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("RasHub database migrations completed");
    }

    public static async Task ProtectLegacyRasGateApiKeysAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var migrator = scope.ServiceProvider
            .GetRequiredService<RasGateApiKeyProtectionMigrator>();

        await migrator.ProtectLegacyKeysAsync(cancellationToken);
    }
}