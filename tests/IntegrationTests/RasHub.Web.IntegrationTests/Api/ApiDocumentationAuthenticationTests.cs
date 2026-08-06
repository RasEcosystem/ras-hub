using System.Net;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RasHub.Web.Authentication;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Api;

[Collection(WebApplicationCollection.Name)]
public sealed class ApiDocumentationAuthenticationTests
{
    [Theory]
    [InlineData("/swagger")]
    [InlineData("/openapi/v1.json")]
    public async Task Documentation_requires_visual_login(string path)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            ApiDocumentationAuthenticationDefaults.LoginPath,
            response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Login_page_is_public_and_does_not_expose_credentials()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            ApiDocumentationAuthenticationDefaults.LoginPath,
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("RasHub API", html);
        Assert.Contains(
            HtmlEncoder.Default.Encode(
                VersionFormatter.Format(
                    typeof(ApiDocumentationOptions).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                        .InformationalVersion)),
            html);
        Assert.Contains("type=\"password\"", html);
        Assert.DoesNotContain(
            RasHubWebApplicationFactory.ApiDocumentationPassword,
            html);
        Assert.Equal("no-store, no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Invalid_credentials_are_rejected_without_cookie()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var form = CreateLoginForm("wrong", "wrong");

        using var response = await client.PostAsync(
            ApiDocumentationAuthenticationDefaults.LoginPath,
            form,
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Authentication failed", html);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task Valid_credentials_unlock_scalar_and_openapi_document()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var form = CreateLoginForm(
            RasHubWebApplicationFactory.ApiDocumentationUsername,
            RasHubWebApplicationFactory.ApiDocumentationPassword);

        using var login = await client.PostAsync(
            ApiDocumentationAuthenticationDefaults.LoginPath,
            form,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/swagger/", login.Headers.Location?.OriginalString);
        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(
            cookies,
            cookie => cookie.StartsWith(
                "RasHub.ApiDocumentation=",
                StringComparison.Ordinal));

        using var scalar = await client.GetAsync(
            "/swagger/",
            TestContext.Current.CancellationToken);
        using var openApi = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        var document = await openApi.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Contains("/api/v1/ras-gates", document);

        using var openApiJson = JsonDocument.Parse(document);
        var controllerTags = openApiJson.RootElement
            .GetProperty("tags")
            .EnumerateArray()
            .ToDictionary(tag => tag.GetProperty("name").GetString()!);

        Assert.Equal(
            "Manage RasGate gateways registered in RasHub.",
            controllerTags["RasGates"].GetProperty("description").GetString());
        Assert.Equal(
            "Inspect the running RasHub service.",
            controllerTags["RasHub"].GetProperty("description").GetString());
    }

    [Fact]
    public async Task Api_key_and_documentation_cookie_do_not_replace_each_other()
    {
        using var factory = CreateFactory();
        using var apiKeyClient = CreateClient(factory);
        apiKeyClient.DefaultRequestHeaders.Add(
            ApiKeyAuthenticationDefaults.HeaderName,
            RasHubWebApplicationFactory.ApiKey);

        using var documentationResponse = await apiKeyClient.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, documentationResponse.StatusCode);

        using var documentationClient = CreateClient(factory);
        using var form = CreateLoginForm(
            RasHubWebApplicationFactory.ApiDocumentationUsername,
            RasHubWebApplicationFactory.ApiDocumentationPassword);
        using var login = await documentationClient.PostAsync(
            ApiDocumentationAuthenticationDefaults.LoginPath,
            form,
            TestContext.Current.CancellationToken);
        using var apiResponse = await documentationClient.GetAsync(
            "/api/v1/ras-hub/status",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_page_removes_documentation_cookie()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var form = CreateLoginForm(
            RasHubWebApplicationFactory.ApiDocumentationUsername,
            RasHubWebApplicationFactory.ApiDocumentationPassword);
        using var login = await client.PostAsync(
            ApiDocumentationAuthenticationDefaults.LoginPath,
            form,
            TestContext.Current.CancellationToken);

        using var logoutPage = await client.GetAsync(
            ApiDocumentationAuthenticationDefaults.LogoutPath,
            TestContext.Current.CancellationToken);
        var html = await logoutPage.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, logoutPage.StatusCode);
        Assert.Contains("Sign out", html);

        using var logout = await client.PostAsync(
            ApiDocumentationAuthenticationDefaults.LogoutPath,
            null,
            TestContext.Current.CancellationToken);
        using var openApi = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(
            ApiDocumentationAuthenticationDefaults.LoginPath,
            logout.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, openApi.StatusCode);
        Assert.Equal(
            ApiDocumentationAuthenticationDefaults.LoginPath,
            openApi.Headers.Location?.AbsolutePath);
    }

    private static RasHubWebApplicationFactory CreateFactory()
    {
        return new RasHubWebApplicationFactory("Development");
    }

    private static HttpClient CreateClient(
        RasHubWebApplicationFactory factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
    }

    private static FormUrlEncodedContent CreateLoginForm(
        string username,
        string password)
    {
        return new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("username", username),
            new KeyValuePair<string, string>("password", password)
        ]);
    }

    public static class VersionFormatter
    {
        public static string Format(string version)
        {
            var parts = version.Split('+', 2);

            if (parts.Length == 2)
            {
                var commit = parts[1];

                if (commit.Length > 7)
                    commit = commit[..7];

                return $"{parts[0]}+{commit}";
            }

            return version;
        }
    }
}