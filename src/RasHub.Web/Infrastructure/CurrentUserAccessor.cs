using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace RasHub.Web.Infrastructure;

public sealed class CurrentUserAccessor(
    AuthenticationStateProvider authenticationStateProvider)
{
    public async Task<string> GetUserIdAsync()
    {
        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();

        var user = authenticationState.User;

        if (user.Identity?.IsAuthenticated != true)
            throw new InvalidOperationException(
                "The current user is not authenticated.");

        return user.FindFirstValue(ClaimTypes.NameIdentifier) ??
               throw new InvalidOperationException(
                   "The authenticated user does not contain a user identifier.");
    }
}
