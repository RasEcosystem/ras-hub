using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Interceptors;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Infrastructure.RasGates;
using RasHub.Infrastructure.RasGates.Serialization;

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

        services.AddDbContext<RasHubDbContext>((serviceProvider, options) =>
        {
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditSoftDeleteInterceptor>());

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(RasHubDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure();
            });
        });

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IRasClusterSnapshotStore, RasClusterSnapshotStore>();
        services.AddScoped<RasClusterQueries>();
        services.AddScoped<RasGateQueries>();

        services.AddSingleton<RasGateHttpClientTransport>();
        services.AddSingleton<RacKeyValueOutputDeserializer>();
        services.AddSingleton<RacClusterOutputDeserializer>();
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
}