using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RasHub.Web.Authentication;
using RasHub.Web.Data;

namespace RasHub.Web.Infrastructure.Authorization;

public sealed record UserAdministrationItem(
    string Id,
    string DisplayName,
    bool IsAdmin,
    bool IsCurrentUser,
    string? ApiKey);

public sealed class UserAdministrationService(
    UserManager<ApplicationUser> userManager,
    AuthenticationStateProvider authenticationStateProvider,
    IAuthorizationService authorizationService)
{
    public async Task<IReadOnlyList<UserAdministrationItem>>
        GetUsersAsync()
    {
        var principal =
            await GetAuthorizedPrincipalAsync();

        var currentUserId =
            userManager.GetUserId(principal);

        var users =
            await userManager.Users
                .AsNoTracking()
                .OrderBy(user => user.Email ?? user.UserName)
                .ToListAsync();

        var result =
            new List<UserAdministrationItem>(users.Count);

        foreach (var user in users)
            result.Add(
                new UserAdministrationItem(
                    user.Id,
                    user.Email
                    ?? user.UserName
                    ?? user.Id,
                    await userManager.IsInRoleAsync(
                        user,
                        AppRoles.Admin),
                    user.Id == currentUserId,
                    user.ApiKey));

        return result;
    }

    public async Task<IdentityResult> SetAdminAsync(
        string userId,
        bool isAdmin)
    {
        var principal =
            await GetAuthorizedPrincipalAsync();

        var user =
            await userManager.FindByIdAsync(userId);

        if (user is null) return Failed("The user no longer exists.");

        var currentlyAdmin =
            await userManager.IsInRoleAsync(
                user,
                AppRoles.Admin);

        if (currentlyAdmin == isAdmin) return IdentityResult.Success;

        if (!isAdmin)
        {
            var currentUserId =
                userManager.GetUserId(principal);

            if (user.Id == currentUserId)
                return Failed(
                    "You cannot remove your own administrator role.");

            var administrators =
                await userManager.GetUsersInRoleAsync(
                    AppRoles.Admin);

            if (administrators.Count <= 1)
                return Failed(
                    "The last administrator cannot be removed.");
        }

        var roleResult = isAdmin
            ? await userManager.AddToRoleAsync(
                user,
                AppRoles.Admin)
            : await userManager.RemoveFromRoleAsync(
                user,
                AppRoles.Admin);

        if (!roleResult.Succeeded) return roleResult;

        return await userManager.UpdateSecurityStampAsync(user);
    }

    public async Task<IdentityResult> GenerateApiKeyAsync(string userId)
    {
        await GetAuthorizedPrincipalAsync();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Failed("The user no longer exists.");

        user.ApiKey = UserApiKeyGenerator.Generate();
        return await userManager.UpdateAsync(user);
    }

    public async Task<IdentityResult> ClearApiKeyAsync(string userId)
    {
        await GetAuthorizedPrincipalAsync();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Failed("The user no longer exists.");

        user.ApiKey = null;
        return await userManager.UpdateAsync(user);
    }

    private async Task<ClaimsPrincipal>
        GetAuthorizedPrincipalAsync()
    {
        var authenticationState =
            await authenticationStateProvider
                .GetAuthenticationStateAsync();

        var authorizationResult =
            await authorizationService.AuthorizeAsync(
                authenticationState.User,
                AppPolicies.ManageGlobalSettings);

        if (!authorizationResult.Succeeded)
            throw new UnauthorizedAccessException(
                "Administrator access is required.");

        return authenticationState.User;
    }

    private static IdentityResult Failed(string description)
    {
        return IdentityResult.Failed(
            new IdentityError
            {
                Code = "AdministrationError",
                Description = description
            });
    }
}