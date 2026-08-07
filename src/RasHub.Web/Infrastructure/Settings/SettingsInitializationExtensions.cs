using Nava.Settings.Abstractions;

namespace RasHub.Web.Infrastructure.Settings;

public static class SettingsInitializationExtensions
{
    public static async Task InitializeRasHubSettingsAsync(
        this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var initializers = scope.ServiceProvider
            .GetServices<IRuntimeSettingsInitializer>();

        foreach (var initializer in initializers)
            await initializer.InitializeAsync();
    }
}