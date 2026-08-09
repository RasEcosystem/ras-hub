using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using RasHub.Web.Authentication;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Api;

[Collection(WebApplicationCollection.Name)]
public sealed partial class ApiDocumentationAuthenticationTests
{
    private const string UserEmail = "documentation@example.test";
    private const string UserPassword = "Documentation-Password-42!";

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/openapi/v1.json")]
    public async Task Documentation_redirects_to_identity_login(string path)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Contains("ReturnUrl=", response.Headers.Location?.Query);
    }

    [Fact]
    public async Task Identity_login_uses_cosmic_rashub_branding()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            "/Account/Login?ReturnUrl=%2Fswagger%2F",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("documentation-login", html);
        Assert.Contains("brand-orbit", html);
        Assert.Contains("RasHub", html);
        Assert.Contains("Development Environment", html);
        Assert.Contains($"v{ThisAssembly.AssemblyFileVersion}", html);
        Assert.DoesNotContain("v@ThisAssembly.AssemblyFileVersion", html);
        Assert.Contains("type=\"password\"", html);
        Assert.Contains("Log in with a passkey", html);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Application_login_is_labeled_as_administration_panel()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            "/Account/Login?ReturnUrl=%2F",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Administration Panel", html);
        Assert.DoesNotContain("Development Environment", html);
    }

    [Fact]
    public async Task Identity_user_unlocks_application_and_documentation()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);
        using var client = CreateClient(factory);

        using var login = await LoginAsync(client, UserEmail, UserPassword);

        Assert.True(
            login.StatusCode == HttpStatusCode.Redirect,
            await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("/swagger/", login.Headers.Location?.AbsolutePath);
        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(
            cookies,
            cookie => cookie.StartsWith(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal));

        using var scalar = await client.GetAsync(
            "/swagger/",
            TestContext.Current.CancellationToken);
        using var openApi = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var application = await client.GetAsync(
            "/",
            TestContext.Current.CancellationToken);
        using var engine = await client.GetAsync(
            "/synchronization-engine",
            TestContext.Current.CancellationToken);
        using var userSettings = await client.GetAsync(
            "/user-settings",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Equal(HttpStatusCode.OK, application.StatusCode);
        Assert.Equal(HttpStatusCode.OK, engine.StatusCode);
        Assert.Equal(HttpStatusCode.OK, userSettings.StatusCode);

        var document = await openApi.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var homePage = await application.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var enginePage = await engine.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var userSettingsPage = await userSettings.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            "/Account/",
            document,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Administration and monitoring", homePage);
        Assert.Contains("Uptime", homePage);
        Assert.Contains("Warnings", homePage);
        Assert.Contains("Errors", homePage);
        Assert.Contains("Online RasGates", homePage);
        Assert.Contains("Health history", homePage);
        Assert.Contains("Application health during the last 24 hours", homePage);
        Assert.DoesNotContain("Monitor workload, queues, workers and task execution", homePage);
        Assert.DoesNotContain("Manage your profile, appearance and API access", homePage);
        Assert.DoesNotContain("Configure global settings and user access", homePage);
        Assert.DoesNotContain("Live workload, workers and task execution state", homePage);
        Assert.Contains("Live workload, workers and task execution state", enginePage);
        Assert.DoesNotContain("Uptime", enginePage);
        Assert.Contains("app-page--wide", homePage);
        Assert.Contains("app-page--wide", enginePage);
        Assert.Contains("app-page--standard", userSettingsPage);
    }

    [Fact]
    public async Task Blocked_identity_user_cannot_log_in()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);
        await factory.SetIdentityUserBlockedAsync(UserEmail, true);
        using var client = CreateClient(factory);

        using var login = await LoginAsync(client, UserEmail, UserPassword);
        var html = await login.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Invalid login attempt", html);
        Assert.False(login.Headers.TryGetValues("Set-Cookie", out var cookies) &&
                     cookies.Any(cookie => cookie.StartsWith(
                         ".AspNetCore.Identity.Application=",
                         StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Blocking_user_revokes_existing_identity_cookie()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);
        using var client = CreateClient(factory);
        using var login = await LoginAsync(client, UserEmail, UserPassword);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        await factory.SetIdentityUserBlockedAsync(UserEmail, true);

        using var response = await client.GetAsync(
            "/swagger/",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Api_key_and_identity_cookie_keep_their_own_scopes()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);

        using var apiKeyClient = CreateClient(factory);
        apiKeyClient.DefaultRequestHeaders.Add(
            ApiKeyAuthenticationDefaults.HeaderName,
            RasHubWebApplicationFactory.ApiKey);
        using var documentationResponse = await apiKeyClient.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, documentationResponse.StatusCode);

        using var identityClient = CreateClient(factory);
        using var login = await LoginAsync(
            identityClient,
            UserEmail,
            UserPassword);
        using var apiResponse = await identityClient.GetAsync(
            "/api/v1/ras-hub/status",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);
    }

    [Fact]
    public async Task OpenApi_error_responses_do_not_expose_success_data_schema()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);
        using var client = CreateClient(factory);
        using var login = await LoginAsync(client, UserEmail, UserPassword);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
        var root = document.RootElement;
        var successSchema = ResolveResponseSchema(
            root,
            "/api/v1/ras-gates/{rasGateId}/clusters/get-paged",
            "post",
            "200");
        var errorSchema = ResolveResponseSchema(
            root,
            "/api/v1/ras-gates/{rasGateId}/clusters/get-paged",
            "post",
            "502");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(successSchema.GetProperty("properties").TryGetProperty("data", out _));

        var errorProperties = errorSchema.GetProperty("properties");
        Assert.True(errorProperties.TryGetProperty("success", out _));
        Assert.True(errorProperties.TryGetProperty("error", out _));
        Assert.True(errorProperties.TryGetProperty("errors", out _));
        Assert.False(errorProperties.TryGetProperty("data", out _));
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        const string loginPath = "/Account/Login?ReturnUrl=%2Fswagger%2F";
        using var page = await client.GetAsync(
            loginPath,
            TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var token = AntiforgeryTokenRegex().Match(html).Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(token));

        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", email),
            new KeyValuePair<string, string>("Input.Password", password),
            new KeyValuePair<string, string>("Input.RememberMe", "false"),
            new KeyValuePair<string, string>("_handler", "login"),
            new KeyValuePair<string, string>("__RequestVerificationToken", WebUtility.HtmlDecode(token))
        ]);

        return await client.PostAsync(
            loginPath,
            form,
            TestContext.Current.CancellationToken);
    }

    private static RasHubWebApplicationFactory CreateFactory()
    {
        return new RasHubWebApplicationFactory("Development");
    }

    private static JsonElement ResolveResponseSchema(
        JsonElement root,
        string path,
        string method,
        string statusCode)
    {
        var schema = root
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("responses")
            .GetProperty(statusCode)
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        while (schema.TryGetProperty("$ref", out var reference))
        {
            schema = root;

            foreach (var segment in reference.GetString()!
                         .Split('/', StringSplitOptions.RemoveEmptyEntries)
                         .Skip(1))
                schema = schema.GetProperty(segment);
        }

        return schema;
    }

    private static HttpClient CreateClient(
        RasHubWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();
}