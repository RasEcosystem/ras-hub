using Microsoft.AspNetCore.Identity;
using RasHub.Web.Data;

namespace RasHub.Web.Infrastructure.Authorization;

public static class AdminRoleInitializer
{
    private const string BootstrapAdminEmailKey = "Authorization:BootstrapAdminEmail";
    private const string BootstrapAdminPasswordKey = "Authorization:BootstrapAdminPassword";
    private const string BootstrapAdminPasswordFileKey = "Authorization:BootstrapAdminPasswordFile";

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
                "No administrator is configured. Set {EmailConfigurationKey} and either " +
                "{PasswordConfigurationKey} or {PasswordFileConfigurationKey} to bootstrap one.",
                BootstrapAdminEmailKey,
                BootstrapAdminPasswordKey,
                BootstrapAdminPasswordFileKey);

            return;
        }

        bootstrapAdminEmail = bootstrapAdminEmail.Trim();
        var bootstrapAdminPassword = await ReadBootstrapPasswordAsync(configuration);

        if (string.IsNullOrEmpty(bootstrapAdminPassword))
            throw new InvalidOperationException(
                $"Either '{BootstrapAdminPasswordKey}' or " +
                $"'{BootstrapAdminPasswordFileKey}' is required when " +
                $"'{BootstrapAdminEmailKey}' is configured and no administrator exists.");

        var user = await userManager.FindByEmailAsync(bootstrapAdminEmail);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = bootstrapAdminEmail, Email = bootstrapAdminEmail, EmailConfirmed = true
            };

            var createUserResult = await userManager.CreateAsync(
                user,
                bootstrapAdminPassword);

            EnsureSucceeded(
                createUserResult,
                "Unable to create the bootstrap administrator account.");

            logger.LogInformation(
                "Created the configured bootstrap administrator account.");
        }
        else if (!await userManager.CheckPasswordAsync(user, bootstrapAdminPassword))
        {
            throw new InvalidOperationException(
                "The configured bootstrap administrator account already exists, " +
                "but its password does not match the configured bootstrap password.");
        }

        var addToRoleResult = await userManager.AddToRoleAsync(user, AppRoles.Admin);

        EnsureSucceeded(
            addToRoleResult,
            $"Unable to assign the '{AppRoles.Admin}' role to the bootstrap administrator.");

        logger.LogInformation(
            "Assigned the {Role} role to the configured bootstrap administrator.",
            AppRoles.Admin);
    }

    private static async Task<string?> ReadBootstrapPasswordAsync(
        IConfiguration configuration)
    {
        var password = configuration[BootstrapAdminPasswordKey];
        var passwordFile = configuration[BootstrapAdminPasswordFileKey];

        if (!string.IsNullOrEmpty(password) && !string.IsNullOrWhiteSpace(passwordFile))
            throw new InvalidOperationException(
                $"Configure either '{BootstrapAdminPasswordKey}' or " +
                $"'{BootstrapAdminPasswordFileKey}', not both.");

        if (string.IsNullOrWhiteSpace(passwordFile))
            return password;

        return (await File.ReadAllTextAsync(passwordFile))
            .TrimEnd('\r', '\n');
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
