using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RasHub.Web.Data;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Authentication;

[Collection(WebApplicationCollection.Name)]
public sealed class LoginSecurityTests
{
    private const string Password = "Correct-Password-42!";

    [Fact]
    public async Task Login_after_five_failed_passwords_locks_account()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        var email = $"lockout-{Guid.NewGuid():N}@example.test";
        await factory.SeedIdentityUserAsync(email, Password);
        using var client = factory.CreateIdentityClient();
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            "/Account/Login");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await PostLoginAsync(
                client,
                email,
                "wrong-password",
                token);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.True(await userManager.IsLockedOutAsync(user));
        }

        using var lockedResponse = await PostLoginAsync(
            client,
            email,
            Password,
            token);

        Assert.Equal(HttpStatusCode.Redirect, lockedResponse.StatusCode);
        Assert.Equal(
            "/Account/Lockout",
            lockedResponse.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Login_more_than_ten_posts_per_minute_returns_too_many_requests()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        using var client = factory.CreateIdentityClient();
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            "/Account/Login");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await PostLoginAsync(
                client,
                "missing@example.test",
                "wrong-password",
                token);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var rejectedResponse = await PostLoginAsync(
            client,
            "missing@example.test",
            "wrong-password",
            token);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejectedResponse.StatusCode);
        Assert.True(rejectedResponse.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password,
        string token)
    {
        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", email),
            new KeyValuePair<string, string>("Input.Password", password),
            new KeyValuePair<string, string>("Input.RememberMe", "false"),
            new KeyValuePair<string, string>("_handler", "login"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]);

        return await client.PostAsync(
            "/Account/Login",
            form,
            TestContext.Current.CancellationToken);
    }
}
