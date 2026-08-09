using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RasHub.Web.Data;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Authentication;

[Collection(WebApplicationCollection.Name)]
public sealed class AdminRoleInitializerTests
{
    private const string AdminEmail = "bootstrap-admin@example.test";
    private const string AdminPassword = "bootstrap-password";

    [Fact]
    public async Task Startup_with_bootstrap_credentials_creates_admin_before_serving_requests()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(AdminEmail);

        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.True(await userManager.CheckPasswordAsync(user, AdminPassword));
        Assert.True(await userManager.IsInRoleAsync(user, AppRoles.Admin));
    }

    [Fact]
    public async Task Startup_with_existing_admin_does_not_require_bootstrap_credentials()
    {
        using var factory = CreateFactory();
        _ = factory.Services;

        await factory.Services.InitializeAdminRoleAsync(
            new ConfigurationBuilder().Build());
    }

    private static RasHubWebApplicationFactory CreateFactory()
    {
        return new RasHubWebApplicationFactory(
            "Testing",
            false,
            new Dictionary<string, string?>
            {
                ["Authorization:BootstrapAdminEmail"] = AdminEmail,
                ["Authorization:BootstrapAdminPassword"] = AdminPassword
            });
    }
}