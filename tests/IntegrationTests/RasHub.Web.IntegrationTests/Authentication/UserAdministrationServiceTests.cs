using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RasHub.Web.Data;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Authentication;

[Collection(WebApplicationCollection.Name)]
public sealed class UserAdministrationServiceTests
{
    [Fact]
    public async Task Administrator_can_block_and_unblock_another_user()
    {
        using var factory = new RasHubWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var administrator = await CreateUserAsync(userManager);
        var target = await CreateUserAsync(userManager);
        Assert.True((await userManager.AddToRoleAsync(
            administrator,
            AppRoles.Admin)).Succeeded);
        var originalSecurityStamp = target.SecurityStamp;
        var service = CreateService(scope.ServiceProvider, userManager, administrator);

        var blockResult = await service.SetBlockedAsync(target.Id, true);

        Assert.True(blockResult.Succeeded);
        var blockedUser = await userManager.FindByIdAsync(target.Id);
        Assert.NotNull(blockedUser);
        Assert.True(blockedUser.IsBlocked);
        Assert.NotEqual(originalSecurityStamp, blockedUser.SecurityStamp);
        var item = Assert.Single(
            await service.GetUsersAsync(),
            user => user.Id == target.Id);
        Assert.True(item.IsBlocked);

        var unblockResult = await service.SetBlockedAsync(target.Id, false);

        Assert.True(unblockResult.Succeeded);
        Assert.False((await userManager.FindByIdAsync(target.Id))!.IsBlocked);
    }

    [Fact]
    public async Task Administrator_cannot_block_own_account()
    {
        using var factory = new RasHubWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var administrator = await CreateUserAsync(userManager);
        Assert.True((await userManager.AddToRoleAsync(
            administrator,
            AppRoles.Admin)).Succeeded);
        var service = CreateService(scope.ServiceProvider, userManager, administrator);

        var result = await service.SetBlockedAsync(administrator.Id, true);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Errors,
            error => error.Description == "You cannot block your own account.");
        Assert.False((await userManager.FindByIdAsync(administrator.Id))!.IsBlocked);
    }

    private static UserAdministrationService CreateService(
        IServiceProvider services,
        UserManager<ApplicationUser> userManager,
        ApplicationUser administrator)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, administrator.Id),
                new Claim(ClaimTypes.Role, AppRoles.Admin)
            ],
            "Test");
        var authenticationStateProvider = new StaticAuthenticationStateProvider(
            new ClaimsPrincipal(identity));

        return new UserAdministrationService(
            services.GetRequiredService<ApplicationDbContext>(),
            userManager,
            authenticationStateProvider,
            services.GetRequiredService<IAuthorizationService>(),
            services.GetRequiredService<ILogger<UserAdministrationService>>());
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> userManager)
    {
        var id = Guid.NewGuid().ToString();
        var user = new ApplicationUser { Id = id, UserName = $"{id}@example.test", Email = $"{id}@example.test" };
        Assert.True((await userManager.CreateAsync(user)).Succeeded);
        return user;
    }

    private sealed class StaticAuthenticationStateProvider(ClaimsPrincipal principal)
        : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(new AuthenticationState(principal));
        }
    }
}
