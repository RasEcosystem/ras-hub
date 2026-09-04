using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Nava.Settings.Abstractions;
using RasHub.Web.Data;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.IntegrationTests.Infrastructure;
using RasHub.Web.Settings;

namespace RasHub.Web.IntegrationTests.Ui;

[Collection(WebApplicationCollection.Name)]
public sealed class UiSmokeTests : IClassFixture<RasHubWebApplicationFactory>
{
    private const string AccountEmail = "account-settings@example.test";
    private const string AccountPassword = "Account-Settings-Password-42!";

    private readonly RasHubWebApplicationFactory _factory;

    public UiSmokeTests(RasHubWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_page_is_available_and_uses_rashub_branding()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            "/Account/Login",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("RasHub", html);
        Assert.Contains("brand-home-link", html);
        Assert.Contains(
            "--mud-palette-primary: rgba(93,143,207,1)",
            html);

        var themePosition = html.IndexOf(
            "--mud-palette-primary: rgba(93,143,207,1)",
            StringComparison.Ordinal);
        var layoutPosition = html.IndexOf(
            "mud-layout",
            StringComparison.Ordinal);

        Assert.True(themePosition >= 0);
        Assert.True(layoutPosition < 0 || themePosition < layoutPosition);
    }

    [Fact]
    public async Task Identity_status_pages_use_the_narrow_page_shell()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            "/Account/AccessDenied",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("app-page--narrow", html);
    }

    [Theory]
    [InlineData(AppTheme.Light, "--mud-palette-background: rgba(245,246,248,1)")]
    [InlineData(AppTheme.System, "@media (prefers-color-scheme: dark)")]
    public async Task Login_page_configured_theme_is_rendered(
        AppTheme theme,
        string expectedThemeCss)
    {
        using var factory = new RasHubWebApplicationFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var settingsProvider = scope.ServiceProvider
                .GetRequiredService<ISettingsProvider<ApplicationSettings>>();

            await settingsProvider.UpdateAsync(new ApplicationSettings { Theme = theme });
        }

        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            "/Account/Login",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedThemeCss, html);
        Assert.Contains(
            "--login-background:var(--mud-palette-background)",
            html);
    }

    [Theory]
    [InlineData("/settings")]
    [InlineData("/health-events")]
    [InlineData("/ras-gates")]
    [InlineData("/ras-endpoints")]
    public async Task Api_key_does_not_authenticate_administration_pages(string path)
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "/Account/Login",
            response.RequestMessage?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task Login_page_exposes_only_password_authentication()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/Account/Login",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Forgot your password?", html);
        Assert.DoesNotContain("Resend email confirmation", html);
        Assert.DoesNotContain("Log in with a passkey", html);
        Assert.DoesNotContain("Create a new account", html);
    }

    [Fact]
    public async Task Account_settings_expose_only_password_and_two_factor_sections()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        await factory.SeedIdentityUserAsync(AccountEmail, AccountPassword);
        using var client = factory.CreateIdentityClient();
        var loginPath =
            "/Account/Login?ReturnUrl=%2FAccount%2FManage%2FChangePassword";
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            loginPath);

        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", AccountEmail),
            new KeyValuePair<string, string>("Input.Password", AccountPassword),
            new KeyValuePair<string, string>("Input.RememberMe", "false"),
            new KeyValuePair<string, string>("_handler", "login"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]);
        using var login = await client.PostAsync(
            loginPath,
            form,
            TestContext.Current.CancellationToken);
        using var response = await client.GetAsync(
            "/Account/Manage/ChangePassword",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Account security", html);
        Assert.Contains("Security settings", html);
        Assert.Contains("Change password", html);
        Assert.Contains("Two-factor authentication", html);
        Assert.Equal(1, html.Split("Back to application").Length - 1);
        Assert.DoesNotContain("Profile", html);
        Assert.DoesNotContain("Passkeys", html);
        Assert.DoesNotContain("Personal data", html);
    }

    [Fact]
    public async Task Application_settings_show_appearance_above_user_administration_without_tabs()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        const string email = "settings-admin@example.test";
        await factory.SeedIdentityUserAsync(email, AccountPassword);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.True((await userManager.AddToRoleAsync(user, AppRoles.Admin)).Succeeded);
        }

        using var client = factory.CreateIdentityClient();
        const string loginPath = "/Account/Login?ReturnUrl=%2FSettings";
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            loginPath);
        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", email),
            new KeyValuePair<string, string>("Input.Password", AccountPassword),
            new KeyValuePair<string, string>("Input.RememberMe", "false"),
            new KeyValuePair<string, string>("_handler", "login"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]);
        using var login = await client.PostAsync(
            loginPath,
            form,
            TestContext.Current.CancellationToken);
        using var response = await client.GetAsync(
            "/Settings",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var appearancePosition = html.IndexOf("Appearance", StringComparison.Ordinal);
        var usersPosition = html.IndexOf("Users", StringComparison.Ordinal);
        Assert.True(appearancePosition >= 0);
        Assert.True(usersPosition > appearancePosition);
        Assert.Contains("Add user", html);
        Assert.DoesNotContain("mud-tabs", html);
    }

    [Fact]
    public async Task Administrator_can_open_RAS_endpoint_management_page()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        const string email = "ras-endpoints-admin@example.test";
        await factory.SeedIdentityUserAsync(email, AccountPassword);
        var gate = await factory.SeedRasGateAsync("Primary Gate");
        _ = await factory.SeedRasEndpointAsync(
            gate.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.True((await userManager.AddToRoleAsync(
                user,
                AppRoles.Admin)).Succeeded);
        }

        using var client = factory.CreateIdentityClient();
        const string loginPath =
            "/Account/Login?ReturnUrl=%2Fras-endpoints";
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            loginPath);
        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", email),
            new KeyValuePair<string, string>("Input.Password", AccountPassword),
            new KeyValuePair<string, string>("Input.RememberMe", "false"),
            new KeyValuePair<string, string>("_handler", "login"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]);
        using var login = await client.PostAsync(
            loginPath,
            form,
            TestContext.Current.CancellationToken);
        using var response = await client.GetAsync(
            "/ras-endpoints",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("RAS endpoints", html);
        Assert.Contains("Production RAS", html);
        Assert.Contains("ras.example.test:1545", html);
        Assert.Contains("Primary Gate", html);
        Assert.Contains("href=\"/ras-endpoints\"", html);
    }
}
