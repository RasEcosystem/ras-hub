using System.Net;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Authentication;

[Collection(WebApplicationCollection.Name)]
public sealed class ForwardedHeadersSecurityTests
{
    private const string Password = "Correct-Password-42!";
    private const string ProxyAddress = "172.31.250.1";

    [Fact]
    public async Task Login_from_trusted_proxy_uses_forwarded_https_for_redirect_and_cookie()
    {
        using var factory = CreateFactory();
        var email = $"forwarded-{Guid.NewGuid():N}@example.test";
        await factory.SeedIdentityUserAsync(email, Password);
        using var client = CreateClient(factory, "198.51.100.10");
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            "/Account/Login");

        using var response = await PostLoginAsync(
            client,
            email,
            Password,
            token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location?.Scheme);
        Assert.Equal("rashub.example.test", response.Headers.Location?.Host);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var applicationCookie = Assert.Single(
            cookies,
            cookie => cookie.StartsWith(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal));
        Assert.Contains("; secure", applicationCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_from_trusted_proxy_partitions_rate_limit_by_forwarded_client()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory, "198.51.100.10");
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

        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.11");

        using var nextClientResponse = await PostLoginAsync(
            client,
            "missing@example.test",
            "wrong-password",
            token);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, nextClientResponse.StatusCode);
    }

    private static RasHubWebApplicationFactory CreateFactory()
    {
        return new RasHubWebApplicationFactory(
            "Testing",
            false,
            new Dictionary<string, string?> { ["ReverseProxy:KnownProxies:0"] = ProxyAddress },
            IPAddress.Parse(ProxyAddress));
    }

    private static HttpClient CreateClient(
        RasHubWebApplicationFactory factory,
        string forwardedClientAddress)
    {
        var client = factory.CreateIdentityClient();
        client.DefaultRequestHeaders.Host = "container.internal";
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedClientAddress);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "rashub.example.test");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        return client;
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
