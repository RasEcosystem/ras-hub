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
    bool IsBlocked,
    string? ApiKey);

public sealed class UserAdministrationService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    AuthenticationStateProvider authenticationStateProvider,
    IAuthorizationService authorizationService,
    ILogger<UserAdministrationService> logger)
{
    public async Task<IReadOnlyList<UserAdministrationItem>> GetUsersAsync()
    {
        var principal = await GetAuthorizedPrincipalAsync();
        var currentUserId = userManager.GetUserId(principal);
        return await dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.Email ?? user.UserName)
            .Select(user => new UserAdministrationItem(
                user.Id,
                user.Email ?? user.UserName ?? user.Id,
                dbContext.UserRoles
                    .Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        dbContext.Roles.Where(role => role.Name == AppRoles.Admin),
                        userRole => userRole.RoleId,
                        role => role.Id,
                        (_, _) => 1)
                    .Any(),
                user.Id == currentUserId,
                user.IsBlocked,
                user.ApiKey))
            .ToListAsync();
    }

    public async Task<IdentityResult> SetAdminAsync(
        string userId,
        bool isAdmin)
    {
        var principal = await GetAuthorizedPrincipalAsync();
        var user = await userManager.FindByIdAsync(userId);

        if (user is null) return Failed("The user no longer exists.");

        var currentlyAdmin = await userManager.IsInRoleAsync(user, AppRoles.Admin);

        if (currentlyAdmin == isAdmin) return IdentityResult.Success;

        if (!isAdmin)
        {
            var currentUserId = userManager.GetUserId(principal);

            if (user.Id == currentUserId)
                return Failed("You cannot remove your own administrator role.");

            var administrators = await userManager.GetUsersInRoleAsync(AppRoles.Admin);

            if (administrators.Count <= 1)
                return Failed("The last administrator cannot be removed.");
        }

        var roleResult = isAdmin
            ? await userManager.AddToRoleAsync(user, AppRoles.Admin)
            : await userManager.RemoveFromRoleAsync(user, AppRoles.Admin);

        if (!roleResult.Succeeded)
        {
            logger.LogWarning(
                "Administrator {ActorUserId} failed to set administrator access " +
                "for user {TargetUserId} to {IsAdmin}: {Errors}",
                userManager.GetUserId(principal),
                user.Id,
                isAdmin,
                string.Join("; ", roleResult.Errors.Select(error => error.Code)));
            return roleResult;
        }

        logger.LogInformation(
            "Administrator {ActorUserId} set administrator access for " +
            "user {TargetUserId} to {IsAdmin}",
            userManager.GetUserId(principal),
            user.Id,
            isAdmin);

        return await userManager.UpdateSecurityStampAsync(user);
    }

    public async Task<IdentityResult> SetBlockedAsync(
        string userId,
        bool isBlocked)
    {
        var principal = await GetAuthorizedPrincipalAsync();
        var user = await userManager.FindByIdAsync(userId);

        if (user is null) return Failed("The user no longer exists.");

        if (user.IsBlocked == isBlocked) return IdentityResult.Success;

        var currentUserId = userManager.GetUserId(principal);

        if (isBlocked && user.Id == currentUserId)
            return Failed("You cannot block your own account.");

        if (isBlocked && await userManager.IsInRoleAsync(user, AppRoles.Admin))
        {
            var administrators = await userManager.GetUsersInRoleAsync(AppRoles.Admin);

            if (administrators.Count(administrator => !administrator.IsBlocked) <= 1)
                return Failed("The last active administrator cannot be blocked.");
        }

        user.IsBlocked = isBlocked;
        var result = await userManager.UpdateSecurityStampAsync(user);

        if (result.Succeeded)
            logger.LogInformation(
                "Administrator {ActorUserId} set blocked state for " +
                "user {TargetUserId} to {IsBlocked}",
                currentUserId,
                user.Id,
                isBlocked);
        else
            logger.LogWarning(
                "Administrator {ActorUserId} failed to set blocked state for " +
                "user {TargetUserId} to {IsBlocked}: {Errors}",
                currentUserId,
                user.Id,
                isBlocked,
                string.Join("; ", result.Errors.Select(error => error.Code)));

        return result;
    }

    public async Task<IdentityResult> GenerateApiKeyAsync(string userId)
    {
        var principal = await GetAuthorizedPrincipalAsync();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Failed("The user no longer exists.");

        user.ApiKey = UserApiKeyGenerator.Generate();
        var result = await userManager.UpdateAsync(user);

        LogApiKeyChange(principal, user, result, "generated");
        return result;
    }

    public async Task<IdentityResult> ClearApiKeyAsync(string userId)
    {
        var principal = await GetAuthorizedPrincipalAsync();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Failed("The user no longer exists.");

        user.ApiKey = null;
        var result = await userManager.UpdateAsync(user);

        LogApiKeyChange(principal, user, result, "revoked");
        return result;
    }

    private async Task<ClaimsPrincipal> GetAuthorizedPrincipalAsync()
    {
        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var authorizationResult = await authorizationService.AuthorizeAsync(
            authenticationState.User,
            AppPolicies.ManageGlobalSettings);

        if (!authorizationResult.Succeeded)
            throw new UnauthorizedAccessException("Administrator access is required.");

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

    private void LogApiKeyChange(
        ClaimsPrincipal principal,
        ApplicationUser user,
        IdentityResult result,
        string operation)
    {
        if (result.Succeeded)
        {
            logger.LogInformation(
                "Administrator {ActorUserId} {Operation} the API key for user {TargetUserId}",
                userManager.GetUserId(principal),
                operation,
                user.Id);
            return;
        }

        logger.LogWarning(
            "Administrator {ActorUserId} failed to change the API key for " +
            "user {TargetUserId}: {Errors}",
            userManager.GetUserId(principal),
            user.Id,
            string.Join("; ", result.Errors.Select(error => error.Code)));
    }
}