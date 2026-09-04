using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace RasHub.Web.Infrastructure.Authorization;

public sealed class AdministrationAuthorizationGuard(
    AuthenticationStateProvider authenticationStateProvider,
    IAuthorizationService authorizationService)
{
    public async Task RequireRasGateManagementAsync()
    {
        _ = await RequireAsync(
            AppPolicies.ManageRasGates,
            "Administrator access is required to manage RasGates.");
    }

    public async Task RequireRasEndpointManagementAsync()
    {
        _ = await RequireAsync(
            AppPolicies.ManageRasEndpoints,
            "Administrator access is required to manage RAS endpoints.");
    }

    public Task<ClaimsPrincipal> RequireGlobalSettingsManagementAsync()
    {
        return RequireAsync(
            AppPolicies.ManageGlobalSettings,
            "Administrator access is required.");
    }

    private async Task<ClaimsPrincipal> RequireAsync(
        string policy,
        string denialMessage)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var result = await authorizationService.AuthorizeAsync(
            state.User,
            policy);

        if (!result.Succeeded)
            throw new UnauthorizedAccessException(denialMessage);

        return state.User;
    }
}
