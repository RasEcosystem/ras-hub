using System.Net;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Authentication;

[Collection(WebApplicationCollection.Name)]
public sealed class IdentitySurfaceSecurityTests
{
    [Theory]
    [InlineData("/Account/Register")]
    [InlineData("/Account/ForgotPassword")]
    [InlineData("/Account/ForgotPasswordConfirmation")]
    [InlineData("/Account/ResetPassword")]
    [InlineData("/Account/ResetPasswordConfirmation")]
    [InlineData("/Account/ResendEmailConfirmation")]
    [InlineData("/Account/ConfirmEmail")]
    [InlineData("/Account/ConfirmEmailChange")]
    [InlineData("/Account/ExternalLogin")]
    [InlineData("/Account/Manage/Email")]
    [InlineData("/Account/Manage/ExternalLogins")]
    [InlineData("/Account/Manage/Passkeys")]
    [InlineData("/Account/Manage/RenamePasskey/test")]
    [InlineData("/Account/Manage/PersonalData")]
    [InlineData("/Account/Manage/DeletePersonalData")]
    public async Task Removed_identity_page_is_not_exposed(string path)
    {
        using var factory = new RasHubWebApplicationFactory(false);
        using var client = factory.CreateIdentityClient();

        using var response = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/Account/PasskeyCreationOptions")]
    [InlineData("/Account/PasskeyRequestOptions")]
    [InlineData("/Account/PerformExternalLogin")]
    [InlineData("/Account/Manage/LinkExternalLogin")]
    [InlineData("/Account/Manage/DownloadPersonalData")]
    public async Task Removed_identity_endpoint_is_not_exposed(string path)
    {
        using var factory = new RasHubWebApplicationFactory(false);
        using var client = factory.CreateIdentityClient();

        using var response = await client.PostAsync(
            path,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
