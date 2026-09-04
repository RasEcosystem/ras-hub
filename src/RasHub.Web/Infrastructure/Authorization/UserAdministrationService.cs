using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Nava.Settings.Abstractions;
using RasHub.Web.Authentication;
using RasHub.Web.Data;
using RasHub.Web.Settings;

namespace RasHub.Web.Infrastructure.Authorization;

public sealed class UserAdministrationItem
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required bool IsAdmin { get; init; }

    public required bool IsCurrentUser { get; init; }

    public required bool IsBlocked { get; init; }

    public string? ApiKey { get; init; }

    public override string ToString()
    {
        return nameof(UserAdministrationItem);
    }
}

public sealed class UserCreationResult(
    IdentityResult identityResult,
    string? initialPassword)
{
    public IdentityResult IdentityResult { get; } = identityResult;

    public string? InitialPassword { get; } = initialPassword;

    public bool Succeeded => IdentityResult.Succeeded;

    public override string ToString()
    {
        return $"{nameof(UserCreationResult)} {{ Succeeded = {Succeeded}, " +
               "InitialPassword = [REDACTED] }";
    }
}

public sealed class UserAdministrationService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    AdministrationAuthorizationGuard authorizationGuard,
    ISettingsStore settingsStore,
    ILogger<UserAdministrationService> logger)
{
    // RasHub currently supports one application replica. Keep administrator
    // count checks and the corresponding mutations in one process-wide
    // critical section so concurrent circuits cannot remove the last active
    // administrator through a write-skew race.
    private static readonly SemaphoreSlim AdministratorMutationLock = new(1, 1);

    public async Task<IReadOnlyList<UserAdministrationItem>> GetUsersAsync()
    {
        var principal = await authorizationGuard
            .RequireGlobalSettingsManagementAsync();
        var currentUserId = userManager.GetUserId(principal);
        return await dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.Email ?? user.UserName)
            .Select(user => new UserAdministrationItem
            {
                Id = user.Id,
                DisplayName = user.Email ?? user.UserName ?? user.Id,
                IsAdmin = dbContext.UserRoles
                    .Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        dbContext.Roles.Where(role => role.Name == AppRoles.Admin),
                        userRole => userRole.RoleId,
                        role => role.Id,
                        (_, _) => 1)
                    .Any(),
                IsCurrentUser = user.Id == currentUserId,
                IsBlocked = user.IsBlocked,
                ApiKey = user.ApiKey
            })
            .ToListAsync();
    }

    public async Task<UserCreationResult> CreateUserAsync(string email)
    {
        var principal = await authorizationGuard
            .RequireGlobalSettingsManagementAsync();
        email = email?.Trim() ?? string.Empty;

        if (email.Length == 0 || !new EmailAddressAttribute().IsValid(email))
            return new UserCreationResult(
                Failed("Enter a valid email address."),
                null);

        var initialPassword = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(24));
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, initialPassword);

        if (result.Succeeded)
        {
            logger.LogInformation(
                "Administrator {ActorUserId} created user {TargetUserId}",
                userManager.GetUserId(principal),
                user.Id);
            return new UserCreationResult(result, initialPassword);
        }

        logger.LogWarning(
            "Administrator {ActorUserId} failed to create a user: {Errors}",
            userManager.GetUserId(principal),
            string.Join("; ", result.Errors.Select(error => error.Code)));
        return new UserCreationResult(result, null);
    }

    public async Task<IdentityResult> SetAdminAsync(
        string userId,
        bool isAdmin)
    {
        var principal = await authorizationGuard
            .RequireGlobalSettingsManagementAsync();
        return await ExecuteAdministratorMutationAsync(async () =>
        {
            var user = await userManager.FindByIdAsync(userId);

            if (user is null) return Failed("The user no longer exists.");

            var currentlyAdmin = await userManager.IsInRoleAsync(user, AppRoles.Admin);

            if (currentlyAdmin == isAdmin) return IdentityResult.Success;

            if (!isAdmin)
            {
                var currentUserId = userManager.GetUserId(principal);

                if (user.Id == currentUserId)
                    return Failed("You cannot remove your own administrator role.");

                if (!user.IsBlocked)
                    if (await GetActiveAdministratorCountAsync() <= 1)
                        return Failed(
                            "The last active administrator cannot be removed.");
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
        });
    }

    public async Task<IdentityResult> SetBlockedAsync(
        string userId,
        bool isBlocked)
    {
        var principal = await authorizationGuard
            .RequireGlobalSettingsManagementAsync();
        return await ExecuteAdministratorMutationAsync(async () =>
        {
            var user = await userManager.FindByIdAsync(userId);

            if (user is null) return Failed("The user no longer exists.");

            if (user.IsBlocked == isBlocked) return IdentityResult.Success;

            var currentUserId = userManager.GetUserId(principal);

            if (isBlocked && user.Id == currentUserId)
                return Failed("You cannot block your own account.");

            if (isBlocked && await userManager.IsInRoleAsync(user, AppRoles.Admin))
                if (await GetActiveAdministratorCountAsync() <= 1)
                    return Failed("The last active administrator cannot be blocked.");

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
        });
    }

    public async Task<IdentityResult> GenerateApiKeyAsync(string userId)
    {
        var principal = await authorizationGuard
            .RequireGlobalSettingsManagementAsync();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Failed("The user no longer exists.");

        user.ApiKey = UserApiKeyGenerator.Generate();
        var result = await userManager.UpdateAsync(user);

        LogApiKeyChange(principal, user, result, "generated");
        return result;
    }

    public async Task<IdentityResult> ClearApiKeyAsync(string userId)
    {
        var principal = await authorizationGuard
            .RequireGlobalSettingsManagementAsync();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return Failed("The user no longer exists.");

        user.ApiKey = null;
        var result = await userManager.UpdateAsync(user);

        LogApiKeyChange(principal, user, result, "revoked");
        return result;
    }

    public async Task<IdentityResult> DeleteUserAsync(string userId)
    {
        var principal = await authorizationGuard
            .RequireGlobalSettingsManagementAsync();
        return await ExecuteAdministratorMutationAsync(async () =>
        {
            var currentUserId = userManager.GetUserId(principal);
            var user = await userManager.FindByIdAsync(userId);

            if (user is null) return Failed("The user no longer exists.");

            if (user.Id == currentUserId)
                return Failed("You cannot delete your own account.");

            if (!user.IsBlocked && await userManager.IsInRoleAsync(user, AppRoles.Admin))
                if (await GetActiveAdministratorCountAsync() <= 1)
                    return Failed("The last active administrator cannot be deleted.");

            var result = await userManager.DeleteAsync(user);

            if (result.Succeeded)
            {
                logger.LogInformation(
                    "Administrator {ActorUserId} deleted user {TargetUserId}",
                    currentUserId,
                    user.Id);
                try
                {
                    await settingsStore.RemoveAsync<UserSettings>(user.Id);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Unable to remove settings for deleted user {TargetUserId}",
                        user.Id);
                }
            }
            else
            {
                logger.LogWarning(
                    "Administrator {ActorUserId} failed to delete user " +
                    "{TargetUserId}: {Errors}",
                    currentUserId,
                    user.Id,
                    string.Join("; ", result.Errors.Select(error => error.Code)));
            }

            return result;
        });
    }

    private static async Task<IdentityResult> ExecuteAdministratorMutationAsync(
        Func<Task<IdentityResult>> mutation)
    {
        await AdministratorMutationLock.WaitAsync();

        try
        {
            return await mutation();
        }
        finally
        {
            AdministratorMutationLock.Release();
        }
    }

    private Task<int> GetActiveAdministratorCountAsync()
    {
        return dbContext.Users
            .AsNoTracking()
            .Where(user => !user.IsBlocked)
            .CountAsync(user => dbContext.UserRoles
                .Where(userRole => userRole.UserId == user.Id)
                .Join(
                    dbContext.Roles.Where(role => role.Name == AppRoles.Admin),
                    userRole => userRole.RoleId,
                    role => role.Id,
                    (_, _) => 1)
                .Any());
    }

    private static IdentityResult Failed(string description)
    {
        return IdentityResult.Failed(
            new IdentityError { Code = "AdministrationError", Description = description });
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
