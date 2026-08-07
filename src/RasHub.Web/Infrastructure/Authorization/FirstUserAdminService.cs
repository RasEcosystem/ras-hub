using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RasHub.Web.Data;

namespace RasHub.Web.Infrastructure.Authorization;

public sealed class FirstUserAdminService(
    UserManager<ApplicationUser> userManager)
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

            return await userManager.AddToRoleAsync(
                user,
                AppRoles.Admin);
        }
        finally
        {
            AssignmentLock.Release();
        }
    }
}