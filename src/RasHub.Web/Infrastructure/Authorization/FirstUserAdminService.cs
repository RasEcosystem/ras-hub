using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RasHub.Web.Data;

namespace RasHub.Web.Infrastructure.Authorization;

public sealed class FirstUserAdminService(
    UserManager<ApplicationUser> userManager,
    ILogger<FirstUserAdminService> logger)
{
    private static readonly SemaphoreSlim AssignmentLock = new(1, 1);

    public async Task<IdentityResult> AssignAdminRoleIfFirstUserAsync(
        ApplicationUser user)
    {
        await AssignmentLock.WaitAsync();

        try
        {
            if (await userManager.Users.CountAsync() != 1)
                return IdentityResult.Success;

            var result = await userManager.AddToRoleAsync(user, AppRoles.Admin);

            if (result.Succeeded)
                logger.LogInformation(
                    "Assigned the {Role} role to the first registered user {UserId}",
                    AppRoles.Admin,
                    user.Id);
            else
                logger.LogWarning(
                    "Failed to assign the {Role} role to the first registered " +
                    "user {UserId}: {Errors}",
                    AppRoles.Admin,
                    user.Id,
                    string.Join("; ", result.Errors.Select(error => error.Code)));

            return result;
        }
        finally
        {
            AssignmentLock.Release();
        }
    }
}
