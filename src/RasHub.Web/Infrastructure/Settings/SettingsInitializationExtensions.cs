using Nava.Settings.Abstractions;

namespace RasHub.Web.Infrastructure.Settings;

public static class SettingsInitializationExtensions
{
    public static async Task InitializeRasHubSettingsAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var initializers = scope.ServiceProvider
            .GetServices<IRuntimeSettingsInitializer>()
            .ToArray();

        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SettingsInitializationExtensions));

        logger.LogInformation(
            "Initializing runtime settings with {InitializerCount} initializers",
            initializers.Length);

        foreach (var initializer in initializers)
            await initializer.InitializeAsync();

        logger.LogInformation("Runtime settings initialization completed");
    }
}
