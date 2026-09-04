using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nava.Settings.Abstractions;
using RasHub.Web.Authentication;
using RasHub.Web.Data;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.IntegrationTests.Infrastructure;
using RasHub.Web.Settings;

namespace RasHub.Web.IntegrationTests.Authentication;

[Collection(WebApplicationCollection.Name)]
public sealed class UserAdministrationServiceTests
{
    [Fact]
    public async Task CreateUser_valid_email_creates_password_user_and_returns_password_once()
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

        var result = await service.CreateUserAsync("new-user@example.test");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.InitialPassword);
        var user = await userManager.FindByEmailAsync("new-user@example.test");
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.True(await userManager.HasPasswordAsync(user));
        Assert.True(await userManager.CheckPasswordAsync(user, result.InitialPassword));
        Assert.False(await userManager.IsInRoleAsync(user, AppRoles.Admin));
        Assert.DoesNotContain(result.InitialPassword, result.ToString());

        using var client = factory.CreateIdentityClient();
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            "/Account/Login");
        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", "new-user@example.test"),
            new KeyValuePair<string, string>("Input.Password", result.InitialPassword),
            new KeyValuePair<string, string>("Input.RememberMe", "false"),
            new KeyValuePair<string, string>("_handler", "login"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]);
        using var login = await client.PostAsync(
            "/Account/Login",
            form,
            TestContext.Current.CancellationToken);
        using var accountSettings = await client.GetAsync(
            "/Account/Manage/ChangePassword",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accountSettings.StatusCode);
    }

    [Fact]
    public async Task CreateUser_invalid_email_rejects_user()
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

        var result = await service.CreateUserAsync("not-an-email");

        Assert.False(result.Succeeded);
        Assert.Null(result.InitialPassword);
        Assert.Contains(
            result.IdentityResult.Errors,
            error => error.Description == "Enter a valid email address.");
    }

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

    [Fact]
    public async Task DeleteUser_other_user_deletes_identity_and_api_key()
    {
        using var factory = new RasHubWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var administrator = await CreateUserAsync(userManager);
        var target = await CreateUserAsync(userManager);
        target.ApiKey = UserApiKeyGenerator.Generate();
        Assert.True((await userManager.UpdateAsync(target)).Succeeded);
        var settingsStore = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
        await settingsStore.SaveAsync(
            new UserSettings { Theme = AppTheme.Light },
            target.Id);
        Assert.True((await userManager.AddToRoleAsync(
            administrator,
            AppRoles.Admin)).Succeeded);
        var service = CreateService(scope.ServiceProvider, userManager, administrator);

        var result = await service.DeleteUserAsync(target.Id);

        Assert.True(result.Succeeded);
        Assert.Null(await userManager.FindByIdAsync(target.Id));
        Assert.DoesNotContain(
            await service.GetUsersAsync(),
            user => user.Id == target.Id);
        Assert.Null(await settingsStore.GetAsync<UserSettings>(target.Id));
    }

    [Fact]
    public async Task DeleteUser_own_account_rejects_deletion()
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

        var result = await service.DeleteUserAsync(administrator.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Errors,
            error => error.Description == "You cannot delete your own account.");
        Assert.NotNull(await userManager.FindByIdAsync(administrator.Id));
    }

    [Fact]
    public async Task DeleteUser_last_active_administrator_rejects_deletion()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var actor = await CreateUserAsync(userManager);
        var target = await CreateUserAsync(userManager);
        Assert.True((await userManager.AddToRoleAsync(actor, AppRoles.Admin)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(target, AppRoles.Admin)).Succeeded);
        actor.IsBlocked = true;
        Assert.True((await userManager.UpdateAsync(actor)).Succeeded);
        Assert.True(await userManager.IsInRoleAsync(target, AppRoles.Admin));
        Assert.Single(
            await userManager.GetUsersInRoleAsync(AppRoles.Admin),
            administrator => !administrator.IsBlocked);
        var service = CreateService(scope.ServiceProvider, userManager, actor);

        var result = await service.DeleteUserAsync(target.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Errors,
            error => error.Description == "The last active administrator cannot be deleted.");
        Assert.NotNull(await userManager.FindByIdAsync(target.Id));
    }

    [Fact]
    public async Task RemoveAdmin_last_active_administrator_rejects_role_removal()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var actor = await CreateUserAsync(userManager);
        var target = await CreateUserAsync(userManager);
        Assert.True((await userManager.AddToRoleAsync(actor, AppRoles.Admin)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(target, AppRoles.Admin)).Succeeded);
        actor.IsBlocked = true;
        Assert.True((await userManager.UpdateAsync(actor)).Succeeded);
        var service = CreateService(scope.ServiceProvider, userManager, actor);

        var result = await service.SetAdminAsync(target.Id, false);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Errors,
            error => error.Description ==
                     "The last active administrator cannot be removed.");
        Assert.True(await userManager.IsInRoleAsync(target, AppRoles.Admin));
    }

    [Theory]
    [InlineData("remove-role")]
    [InlineData("block")]
    [InlineData("delete")]
    public async Task Concurrent_administrator_reductions_preserve_one_active_administrator(
        string operation)
    {
        using var factory = new RasHubWebApplicationFactory(false);
        string firstAdministratorId;
        string secondAdministratorId;

        using (var setupScope = factory.Services.CreateScope())
        {
            var setupUserManager = setupScope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var firstSetupAdministrator = await CreateUserAsync(setupUserManager);
            var secondSetupAdministrator = await CreateUserAsync(setupUserManager);
            Assert.True((await setupUserManager.AddToRoleAsync(
                firstSetupAdministrator,
                AppRoles.Admin)).Succeeded);
            Assert.True((await setupUserManager.AddToRoleAsync(
                secondSetupAdministrator,
                AppRoles.Admin)).Succeeded);
            firstAdministratorId = firstSetupAdministrator.Id;
            secondAdministratorId = secondSetupAdministrator.Id;
        }

        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var firstUserManager = firstScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var secondUserManager = secondScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var firstAdministrator = await firstUserManager.FindByIdAsync(
            firstAdministratorId);
        var secondAdministrator = await secondUserManager.FindByIdAsync(
            secondAdministratorId);
        Assert.NotNull(firstAdministrator);
        Assert.NotNull(secondAdministrator);
        var firstService = CreateService(
            firstScope.ServiceProvider,
            firstUserManager,
            firstAdministrator);
        var secondService = CreateService(
            secondScope.ServiceProvider,
            secondUserManager,
            secondAdministrator);

        var results = await Task.WhenAll(
            ReduceAdministratorAsync(
                firstService,
                secondAdministratorId,
                operation),
            ReduceAdministratorAsync(
                secondService,
                firstAdministratorId,
                operation));

        Assert.Single(results, result => result.Succeeded);
        using var assertionScope = factory.Services.CreateScope();
        var assertionUserManager = assertionScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single(
            await assertionUserManager.GetUsersInRoleAsync(AppRoles.Admin),
            administrator => !administrator.IsBlocked);
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
            services.GetRequiredService<ISettingsStore>(),
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

    private static Task<IdentityResult> ReduceAdministratorAsync(
        UserAdministrationService service,
        string userId,
        string operation)
    {
        return operation switch
        {
            "remove-role" => service.SetAdminAsync(userId, false),
            "block" => service.SetBlockedAsync(userId, true),
            "delete" => service.DeleteUserAsync(userId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
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
