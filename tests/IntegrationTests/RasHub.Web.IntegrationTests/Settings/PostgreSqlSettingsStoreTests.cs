using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nava.Settings.Abstractions;
using RasHub.Infrastructure.Database;
using RasHub.Web.IntegrationTests.Infrastructure;
using RasHub.Web.Settings;

namespace RasHub.Web.IntegrationTests.Settings;

[Collection(WebApplicationCollection.Name)]
public sealed class PostgreSqlSettingsStoreTests
    : IClassFixture<RasHubWebApplicationFactory>
{
    private readonly RasHubWebApplicationFactory _factory;

    public PostgreSqlSettingsStoreTests(RasHubWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Runtime_settings_are_persisted_with_the_nava_key()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
        var dbContext = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();

        await store.SaveAsync(new ApplicationSettings
        {
            Theme = AppTheme.Slate
        });

        var entry = await dbContext.Settings
            .AsNoTracking()
            .SingleAsync(
                item => item.Key == "app-settings",
                TestContext.Current.CancellationToken);
        var restored = await store.GetAsync<ApplicationSettings>();

        Assert.Contains("\"theme\":1", entry.Value);
        Assert.Equal(AppTheme.Slate, restored?.Theme);

        await store.RemoveAsync<ApplicationSettings>();
        Assert.Null(await store.GetAsync<ApplicationSettings>());
    }

    [Fact]
    public async Task User_settings_are_isolated_by_scope()
    {
        const string firstUser = "user-1";
        const string secondUser = "user-2";

        await using var scope = _factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();

        await store.SaveAsync(
            new UserSettings { Theme = AppTheme.Carbon },
            firstUser);
        await store.SaveAsync(
            new UserSettings { Theme = AppTheme.Slate },
            secondUser);

        Assert.Equal(
            AppTheme.Carbon,
            (await store.GetAsync<UserSettings>(firstUser))?.Theme);
        Assert.Equal(
            AppTheme.Slate,
            (await store.GetAsync<UserSettings>(secondUser))?.Theme);

        await store.RemoveAsync<UserSettings>(firstUser);
        await store.RemoveAsync<UserSettings>(secondUser);
    }
}