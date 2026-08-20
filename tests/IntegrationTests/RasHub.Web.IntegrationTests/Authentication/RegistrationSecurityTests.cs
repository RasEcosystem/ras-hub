using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Nava.Settings.Abstractions;
using RasHub.Web.Data;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.IntegrationTests.Infrastructure;
using RasHub.Web.Settings;

namespace RasHub.Web.IntegrationTests.Authentication;

[Collection(WebApplicationCollection.Name)]
public sealed class RegistrationSecurityTests
{
    private const string Password = "registration-password";

    [Fact]
    public async Task Registration_page_when_disabled_redirects_to_login()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        using var client = factory.CreateIdentityClient();

        using var response = await client.GetAsync(
            "/Account/Register",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Registration_post_disabled_after_form_load_does_not_create_user()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        await SetRegistrationAsync(factory, true);
        using var client = factory.CreateIdentityClient();
        var email = $"blocked-{Guid.NewGuid():N}@example.test";
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            "/Account/Register");

        await SetRegistrationAsync(factory, false);
        using var response = await PostRegistrationAsync(client, email, token);

        var responseBody = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect,
            $"Expected a redirect, got {response.StatusCode}: {responseBody}");
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await userManager.FindByEmailAsync(email));
    }

    [Fact]
    public async Task First_registered_user_when_registration_enabled_is_not_admin()
    {
        using var factory = new RasHubWebApplicationFactory(false);
        await SetRegistrationAsync(factory, true);
        using var client = factory.CreateIdentityClient();
        var email = $"first-{Guid.NewGuid():N}@example.test";
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            "/Account/Register");

        using var response = await PostRegistrationAsync(client, email, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.False(await userManager.IsInRoleAsync(user, AppRoles.Admin));
    }

    private static async Task SetRegistrationAsync(
        RasHubWebApplicationFactory factory,
        bool allowed)
    {
        using var scope = factory.Services.CreateScope();
        var settingsProvider = scope.ServiceProvider
            .GetRequiredService<ISettingsProvider<ApplicationSettings>>();

        await settingsProvider.UpdateAsync(new ApplicationSettings { AllowRegistration = allowed });
    }

    private static async Task<HttpResponseMessage> PostRegistrationAsync(
        HttpClient client,
        string email,
        string token)
    {
        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", email),
            new KeyValuePair<string, string>("Input.Password", Password),
            new KeyValuePair<string, string>("Input.ConfirmPassword", Password),
            new KeyValuePair<string, string>("_handler", "register"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]);

        return await client.PostAsync(
            "/Account/Register",
            form,
            TestContext.Current.CancellationToken);
    }
}