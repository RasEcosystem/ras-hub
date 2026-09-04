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
    public async Task Login_with_default_bootstrap_email_succeeds()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        const string email = "rashub@rashub";
        await factory.SeedIdentityUserAsync(email, Password);
        using var client = factory.CreateIdentityClient();
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            "/Account/Login");

        using var response = await PostLoginAsync(
            client,
            email,
            Password,
            token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.AbsolutePath);
    }

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

    [Fact]
    public async Task Login_equivalent_route_variants_share_rate_limit_bucket()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        using var client = factory.CreateIdentityClient();
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            "/Account/Login");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await PostLoginAsync(
                client,
                "missing@example.test",
                "wrong-password",
                token);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await PostLoginAsync(
                client,
                "missing@example.test",
                "wrong-password",
                token,
                "/account/login/");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var rejectedResponse = await PostLoginAsync(
            client,
            "missing@example.test",
            "wrong-password",
            token,
            "/ACCOUNT/LOGIN/");

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejectedResponse.StatusCode);
    }

    [Theory]
    [InlineData("//evil.example/phish")]
    [InlineData("https://evil.example/phish")]
    [InlineData("/\\evil.example/phish")]
    [InlineData("%2f%2fevil.example%2fphish")]
    public async Task Login_external_return_url_stays_inside_application(
        string returnUrl)
    {
        using var factory = new RasHubWebApplicationFactory(false);
        var email = $"redirect-{Guid.NewGuid():N}@example.test";
        await factory.SeedIdentityUserAsync(email, Password);
        using var client = factory.CreateIdentityClient();
        var loginPath =
            $"/Account/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}";
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            loginPath);

        using var response = await PostLoginAsync(
            client,
            email,
            Password,
            token,
            loginPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("localhost", response.Headers.Location?.Host);
    }

    [Fact]
    public async Task Login_local_return_url_preserves_path_and_query()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        var email = $"local-redirect-{Guid.NewGuid():N}@example.test";
        await factory.SeedIdentityUserAsync(email, Password);
        using var client = factory.CreateIdentityClient();
        const string loginPath =
            "/Account/Login?ReturnUrl=%2Fsafe%2Ftarget%3Fpage%3D2";
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            loginPath);

        using var response = await PostLoginAsync(
            client,
            email,
            Password,
            token,
            loginPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("localhost", response.Headers.Location?.Host);
        Assert.Equal("/safe/target", response.Headers.Location?.AbsolutePath);
        Assert.Equal("?page=2", response.Headers.Location?.Query);
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password,
        string token,
        string path = "/Account/Login")
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
            path,
            form,
            TestContext.Current.CancellationToken);
    }
}
