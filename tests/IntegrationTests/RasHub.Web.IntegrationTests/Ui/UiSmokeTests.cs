using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Nava.Settings.Abstractions;
using RasHub.Web.IntegrationTests.Infrastructure;
using RasHub.Web.Settings;

namespace RasHub.Web.IntegrationTests.Ui;

[Collection(WebApplicationCollection.Name)]
public sealed class UiSmokeTests : IClassFixture<RasHubWebApplicationFactory>
{
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
    public async Task Api_key_does_not_authenticate_the_user_interface()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            "/settings",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "/Account/Login",
            response.RequestMessage?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task Disabled_authentication_options_are_hidden_from_login_page()
    {
        using var factory = new RasHubWebApplicationFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var settingsProvider = scope.ServiceProvider
                .GetRequiredService<ISettingsProvider<ApplicationSettings>>();

            await settingsProvider.UpdateAsync(new ApplicationSettings
            {
                AllowForgotPassword = false,
                AllowResendEmailConfirmation = false,
                AllowPasskeyLogin = false,
                AllowRegistration = false
            });
        }

        using var client = factory.CreateClient();
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
}