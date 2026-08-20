using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RasHub.Web.Authentication;
using RasHub.Web.Data;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Authentication;

[Collection(WebApplicationCollection.Name)]
public sealed class UserApiKeyAuthenticationTests
{
    [Fact]
    public async Task Generated_key_grants_access_and_cleared_key_revokes_it()
    {
        using var factory = new RasHubWebApplicationFactory();
        var apiKey = UserApiKeyGenerator.Generate();
        var userId = Guid.NewGuid().ToString();

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var result = await userManager.CreateAsync(new ApplicationUser
            {
                Id = userId,
                UserName = $"{userId}@example.test",
                Email = $"{userId}@example.test",
                ApiKey = apiKey
            });
            Assert.True(result.Succeeded);
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            ApiKeyAuthenticationDefaults.HeaderName,
            apiKey);

        using var allowed = await client.GetAsync(
            "/api/v1/info",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            Assert.NotNull(user);
            user.ApiKey = null;
            Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        }

        using var revoked = await client.GetAsync(
            "/api/v1/info",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task Blocked_user_api_key_is_rejected_and_unblocking_restores_access()
    {
        using var factory = new RasHubWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        await factory.SetIdentityUserBlockedAsync("api-user@example.test", true);

        using var blocked = await client.GetAsync(
            "/api/v1/info",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);

        await factory.SetIdentityUserBlockedAsync("api-user@example.test", false);

        using var unblocked = await client.GetAsync(
            "/api/v1/info",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, unblocked.StatusCode);
    }

    [Fact]
    public async Task Password_policy_only_requires_eight_characters()
    {
        using var factory = new RasHubWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        var accepted = await userManager.CreateAsync(
            CreateUser(),
            "abcdefgh");
        var rejected = await userManager.CreateAsync(
            CreateUser(),
            "abcdefg");

        Assert.True(accepted.Succeeded);
        Assert.False(rejected.Succeeded);
        Assert.Contains(
            rejected.Errors,
            error => error.Code == nameof(IdentityErrorDescriber.PasswordTooShort));
    }

    private static ApplicationUser CreateUser()
    {
        var id = Guid.NewGuid().ToString();
        return new ApplicationUser { Id = id, UserName = $"{id}@example.test", Email = $"{id}@example.test" };
    }
}
