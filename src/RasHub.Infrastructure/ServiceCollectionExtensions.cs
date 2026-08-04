using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RasHub.Application.Interfaces;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Interceptors;

namespace RasHub.Infrastructure;

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

        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}