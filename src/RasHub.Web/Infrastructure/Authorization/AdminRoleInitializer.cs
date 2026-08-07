using Microsoft.AspNetCore.Identity;
using RasHub.Web.Data;

namespace RasHub.Web.Infrastructure.Authorization;

public static class AdminRoleInitializer
{
    private const string BootstrapAdminEmailKey = "Authorization:BootstrapAdminEmail";

    public static async Task InitializeAdminRoleAsync(
        this IServiceProvider services,
        IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AdminRoleInitializer));

        if (!await roleManager.RoleExistsAsync(AppRoles.Admin))
        {
            var createRoleResult = await roleManager.CreateAsync(new IdentityRole(AppRoles.Admin));

            EnsureSucceeded(
                createRoleResult,
                $"Unable to create the '{AppRoles.Admin}' role.");
        }

        var administrators = await userManager.GetUsersInRoleAsync(AppRoles.Admin);

        if (administrators.Count > 0) return;

        var bootstrapAdminEmail = configuration[BootstrapAdminEmailKey];

        if (string.IsNullOrWhiteSpace(bootstrapAdminEmail))
        {
            logger.LogWarning(
                "No administrator is configured. Set {ConfigurationKey} " +
                "to the email of an existing user and restart the application.",
                BootstrapAdminEmailKey);

            return;
        }

        var user = await userManager.FindByEmailAsync(bootstrapAdminEmail.Trim());

        if (user is null)
        {
            logger.LogWarning(
                "The bootstrap administrator {Email} does not exist. " +
                "Create the account and restart the application.",
                bootstrapAdminEmail);

            return;
        }

        var addToRoleResult = await userManager.AddToRoleAsync(user, AppRoles.Admin);

        EnsureSucceeded(
            addToRoleResult,
            $"Unable to assign the '{AppRoles.Admin}' role to " +
            $"'{bootstrapAdminEmail}'.");

        logger.LogInformation(
            "Assigned the {Role} role to {Email}.",
            AppRoles.Admin,
            bootstrapAdminEmail);
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        string message)
    {
        if (result.Succeeded) return;

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));

        throw new InvalidOperationException($"{message} {errors}");
    }
}