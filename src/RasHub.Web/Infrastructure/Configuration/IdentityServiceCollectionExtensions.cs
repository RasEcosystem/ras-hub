using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RasHub.Infrastructure.Database;
using RasHub.Web.Authentication;
using RasHub.Web.Components.Account;
using RasHub.Web.Data;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.Infrastructure.Configuration;

internal static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddRasHubIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.Scheme,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                ApiDocumentationAuthenticationDefaults.Policy,
                policy => policy.RequireAuthenticatedUser());
            options.AddPolicy(
                AppPolicies.ManageGlobalSettings,
                policy => policy.RequireRole(AppRoles.Admin));
            options.AddPolicy(
                AppPolicies.ManageRasGates,
                policy => policy.RequireRole(AppRoles.Admin));
            options.AddPolicy(
                AppPolicies.ManageRasEndpoints,
                policy => policy.RequireRole(AppRoles.Admin));
        });

        services.AddCascadingAuthenticationState();
        services.AddScoped<IdentityRedirectManager>();
        services.AddScoped<AuthenticationStateProvider,
            IdentityRevalidatingAuthenticationStateProvider>();

        var connectionString = configuration.GetConnectionString(
            RasHubDbContext.ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{RasHubDbContext.ConnectionStringName}' is required.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__IdentityMigrationsHistory");
                }));
        services.AddDatabaseDeveloperPageExceptionFilter();
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<ApplicationSignInManager>()
            .AddDefaultTokenProviders();
        services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnValidatePrincipal = async context =>
            {
                await SecurityStampValidator.ValidatePrincipalAsync(context);

                if (context.Principal?.Identity?.IsAuthenticated != true)
                    return;

                var userManager = context.HttpContext.RequestServices
                    .GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.GetUserAsync(context.Principal);

                if (user?.IsBlocked != true)
                    return;

                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(
                    IdentityConstants.ApplicationScheme);
            };
        });
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<AdministrationAuthorizationGuard>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<UserApiKeyService>();

        return services;
    }
}
