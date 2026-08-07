using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RasHub.Web.Data;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Authentication;

[Collection(WebApplicationCollection.Name)]
public sealed class FirstUserAdminServiceTests
{
    [Fact]
    public async Task First_registered_user_becomes_admin()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var service = scope.ServiceProvider
            .GetRequiredService<FirstUserAdminService>();
        var user = CreateUser();

        Assert.True((await userManager.CreateAsync(user)).Succeeded);
        Assert.True((await service.AssignAdminRoleIfFirstUserAsync(user)).Succeeded);

        Assert.True(await userManager.IsInRoleAsync(user, AppRoles.Admin));
    }

    [Fact]
    public async Task Later_registered_users_do_not_become_admins()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var service = scope.ServiceProvider
            .GetRequiredService<FirstUserAdminService>();
        var first = CreateUser();
        var second = CreateUser();

        Assert.True((await userManager.CreateAsync(first)).Succeeded);
        Assert.True((await service.AssignAdminRoleIfFirstUserAsync(first)).Succeeded);
        Assert.True((await userManager.CreateAsync(second)).Succeeded);
        Assert.True((await service.AssignAdminRoleIfFirstUserAsync(second)).Succeeded);

        Assert.False(await userManager.IsInRoleAsync(second, AppRoles.Admin));
    }

    private static ApplicationUser CreateUser()
    {
        var id = Guid.NewGuid().ToString();
        return new ApplicationUser
        {
            Id = id,
            UserName = $"{id}@example.test",
            Email = $"{id}@example.test"
        };
    }
}