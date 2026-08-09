using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Nava.Settings.Abstractions;
using Nava.Settings.Extensions;
using RasHub.Application.RasGates.Tasks;
using RasHub.Contracts.Common;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Extensions;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Configuration;
using RasHub.Web.Api;
using RasHub.Web.Api.Filters;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Authentication;
using RasHub.Web.Components;
using RasHub.Web.Components.Account;
using RasHub.Web.Data;
using RasHub.Web.Infrastructure;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.Infrastructure.Diagnostics;
using RasHub.Web.Infrastructure.RasGates;
using RasHub.Web.Infrastructure.Settings;
using RasHub.Web.Infrastructure.Themes.Providers;
using RasHub.Web.Middlewares;
using RasHub.Web.Settings;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

namespace RasHub.Web;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            await RunAsync(args);
            return 0;
        }
        catch (Exception exception) when (exception is not HostAbortedException)
        {
            Log.Fatal(exception, "RasHub terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var applicationDiagnostics = new ApplicationDiagnostics(TimeProvider.System);

        builder.Services.AddSingleton(applicationDiagnostics);

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty(
                    "Environment",
                    context.HostingEnvironment.EnvironmentName)
                .WriteTo.Sink(applicationDiagnostics);
        });

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        builder.Services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.Scheme,
                _ => { });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                ApiDocumentationAuthenticationDefaults.Policy,
                policy => policy.RequireAuthenticatedUser());
            options.AddPolicy(
                AppPolicies.ManageGlobalSettings,
                policy => policy.RequireRole(AppRoles.Admin));
        });

        builder.Services.AddMudServices();
        builder.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider,
            IdentityRevalidatingAuthenticationStateProvider>();

        var rasHubConnectionString = builder.Configuration
            .GetConnectionString(RasHubDbContext.ConnectionStringName);

        if (string.IsNullOrWhiteSpace(rasHubConnectionString))
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{RasHubDbContext.ConnectionStringName}' is required.");

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(rasHubConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__IdentityMigrationsHistory");
            }));
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();
        builder.Services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
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
        builder.Services.ConfigureApplicationCookie(options =>
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
        builder.Services.AddSingleton<IEmailSender<ApplicationUser>,
            IdentityNoOpEmailSender>();

        builder.Services.AddScoped<ISettingsStore, PostgreSqlSettingsStore>();
        builder.Services.AddRuntimeSettings<ApplicationSettings>();
        builder.Services.AddScopedSettings<UserSettings>();
        builder.Services.AddSingleton<ThemeProvider>();
        builder.Services.AddScoped<CurrentUserAccessor>();
        builder.Services.AddScoped<IUserSettingsProvider, UserSettingsProvider>();
        builder.Services.AddScoped<UserAdministrationService>();
        builder.Services.AddScoped<FirstUserAdminService>();
        builder.Services.AddScoped<UserApiKeyService>();

        builder.Services.ConfigureReverseProxy(builder.Configuration);
        builder.Services.ConfigureApi();
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<ApiKeySecurityTransformer>();
            options.AddDocumentTransformer<ControllerDescriptionTransformer>();
            options.AddOperationTransformer<ApiKeySecurityTransformer>();
        });

        builder.Services.AddRasHubInfrastructure(builder.Configuration);

        builder.Services.AddRasHubSynchronization(options =>
        {
            builder.Configuration
                .GetSection(SynchronizationEngineOptions.SectionName)
                .Bind(options);
        });
        builder.Services.AddScoped<
            IBackgroundTaskHandler<RefreshRasGateStatusTask>,
            RefreshRasGateStatusTaskHandler>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<SynchronizeClustersTask>,
            SynchronizeClustersTaskHandler>();
        builder.Services
            .AddOptions<RasGateMonitoringOptions>()
            .Bind(builder.Configuration.GetSection(
                RasGateMonitoringOptions.SectionName))
            .Validate(
                value => value.PollInterval > TimeSpan.Zero,
                "RasGate monitoring poll interval must be positive.")
            .Validate(
                value => value.OnlineThreshold >= value.PollInterval,
                "RasGate online threshold must not be shorter than the poll interval.")
            .Validate(
                value => value.RequestTimeout > TimeSpan.Zero,
                "RasGate monitoring request timeout must be positive.")
            .ValidateOnStart();
        builder.Services.AddHostedService<RasGateMonitoringService>();

        builder.Services
            .AddHealthChecks()
            .AddDbContextCheck<RasHubDbContext>("database", tags: ["ready"]);

        var app = builder.Build();

        var migrateOnly = string.Equals(app.Configuration["mode"], "migrate",
            StringComparison.OrdinalIgnoreCase);
        var applyMigrations = app.Configuration.GetValue<bool>("Database:ApplyMigrations");

        if (migrateOnly || applyMigrations)
        {
            await app.Services.MigrateRasHubDatabaseAsync();
            await app.Services.MigrateIdentityDatabaseAsync();
        }

        if (migrateOnly)
            return;

        if (!applyMigrations)
            await app.Services.MigrateIdentityDatabaseAsync();
        if (app.Configuration.GetValue("Settings:InitializeOnStart", true))
            await app.Services.InitializeRasHubSettingsAsync();
        await app.Services.InitializeAdminRoleAsync(app.Configuration);

        app.UseForwardedHeaders();
        app.ConfigureLogging();
        app.ConfigurePipeline();
        app.ConfigureLifecycleLogging();

        await app.RunAsync();
    }
}

internal static class ApplicationConfigurationExtensions
{
    public static void ConfigureReverseProxy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedHost |
                ForwardedHeaders.XForwardedProto;

            foreach (var proxy in configuration
                         .GetSection("ReverseProxy:KnownProxies")
                         .GetChildren())
            {
                if (!IPAddress.TryParse(proxy.Value, out var address))
                    throw new InvalidOperationException(
                        $"Invalid trusted reverse proxy address: {proxy.Value}");

                options.KnownProxies.Add(address);

                var mappedAddress = address.MapToIPv6();

                if (!mappedAddress.Equals(address))
                    options.KnownProxies.Add(mappedAddress);
            }
        });
    }

    public static void ConfigureApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
            ApiJson.Configure(options.SerializerOptions));

        services.AddRouting(options =>
        {
            options.LowercaseUrls = true;
            options.LowercaseQueryStrings = true;
        });

        services.Configure<MvcOptions>(options => { options.Filters.Add(new ProducesAttribute("application/json")); });

        services
            .AddControllers(options => { options.Filters.Add<ApiResponseResultFilter>(); })
            .AddJsonOptions(options => { ApiJson.Configure(options.JsonSerializerOptions); })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .SelectMany(entry => entry.Value!.Errors.Select(error =>
                            new ApiError("validation_error", error.ErrorMessage, entry.Key)))
                        .ToList();

                    return new BadRequestObjectResult(
                        ApiResponse<object>.Fail(HttpStatusCode.BadRequest, errors));
                };
            });
    }

    public static void ConfigureLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (http, _, exception) =>
            {
                if (exception is not null ||
                    http.Response.StatusCode >= StatusCodes.Status500InternalServerError)
                    return LogEventLevel.Error;

                var isControllerRequest = http.GetEndpoint()?
                    .Metadata
                    .GetMetadata<ControllerActionDescriptor>() is not null;

                return isControllerRequest
                    ? LogEventLevel.Information
                    : LogEventLevel.Verbose;
            };

            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded " +
                                      "{StatusCode} in {Elapsed:0.0000} ms";

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("TraceId", ApiTrace.GetTraceId(httpContext));
                diagnosticContext.Set("Phase", "HTTP");

                if (httpContext.Connection.RemoteIpAddress is not null)
                    diagnosticContext.Set(
                        "RemoteIP",
                        httpContext.Connection.RemoteIpAddress.ToString());
            };
        });
    }

    public static void ConfigurePipeline(this WebApplication app)
    {
        app.UseApiExceptionHandling();
        app.UseApiStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error", true);
            app.UseHsts();
        }

        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/api"),
            branch => branch.UseStatusCodePagesWithReExecute(
                "/not-found",
                createScopeForStatusCodePages: true));
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi()
                .RequireAuthorization(
                    ApiDocumentationAuthenticationDefaults.Policy);

            app.MapScalarApiReference("/swagger", options =>
            {
                options.EnabledTargets =
                [
                    ScalarTarget.Shell,
                    ScalarTarget.Php,
                    ScalarTarget.Python
                ];

                options
                    .WithTitle("RasHub API")
                    .WithTheme(ScalarTheme.DeepSpace)
                    .ForceDarkMode()
                    .AddPreferredSecuritySchemes(ApiKeyAuthenticationDefaults.Scheme)
                    .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl)
                    .HideClientButton()
                    .HideDeveloperTools()
                    .DisableMcp()
                    .DisableAgent();
            }).RequireAuthorization(
                ApiDocumentationAuthenticationDefaults.Policy);
        }

        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapControllers();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
        app.MapAdditionalIdentityEndpoints();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = _ => false
            })
            .AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready")
            })
            .AllowAnonymous();
    }

    public static async Task MigrateIdentityDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.MigrateAsync(cancellationToken);
        else
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    public static void ConfigureLifecycleLogging(this WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()?
                .Addresses ?? app.Urls;

            app.Logger.LogInformation(
                "RasHub started successfully and is listening on {Addresses}",
                string.Join(", ", addresses));

            if (app.Environment.IsDevelopment())
            {
                var apiReferenceUrls = addresses.Select(address =>
                    $"{address.TrimEnd('/')}/swagger/");

                app.Logger.LogInformation(
                    "Scalar API reference is available at {ApiReferenceUrls}",
                    string.Join(", ", apiReferenceUrls));
            }
        });

        app.Lifetime.ApplicationStopped.Register(() =>
            app.Logger.LogInformation("RasHub stopped successfully"));
    }
}