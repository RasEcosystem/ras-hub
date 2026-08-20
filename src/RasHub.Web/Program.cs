using MudBlazor.Services;
using Nava.Settings.Abstractions;
using Nava.Settings.Extensions;
using RasHub.Application.RasGates.Tasks.Clusters;
using RasHub.Application.RasGates.Tasks.Infobases;
using RasHub.Application.RasGates.Tasks.Status;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Configuration;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Extensions;
using RasHub.Web.Api;
using RasHub.Web.Infrastructure;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.Infrastructure.Configuration;
using RasHub.Web.Infrastructure.Database;
using RasHub.Web.Infrastructure.Diagnostics;
using RasHub.Web.Infrastructure.RasGates;
using RasHub.Web.Infrastructure.Security;
using RasHub.Web.Infrastructure.Settings;
using RasHub.Web.Infrastructure.Themes.Providers;
using RasHub.Web.Settings;
using Serilog;

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

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ApplicationDiagnostics>();
        builder.Services.AddRasHubOpenTelemetry(builder.Configuration);
        builder.Services.AddRasHubDataProtection(builder.Configuration);
        builder.Services.AddAuthenticationRateLimiting();

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty(
                    "Environment",
                    context.HostingEnvironment.EnvironmentName)
                .WriteTo.Sink(
                    services.GetRequiredService<ApplicationDiagnostics>());
        });

        builder.Services.AddRasHubIdentity(builder.Configuration);

        builder.Services.AddMudServices();
        builder.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddScoped<ISettingsStore, PostgreSqlSettingsStore>();
        builder.Services.AddRuntimeSettings<ApplicationSettings>();
        builder.Services.AddScopedSettings<UserSettings>();
        builder.Services.AddSingleton<ThemeProvider>();
        builder.Services.AddScoped<IUserSettingsProvider, UserSettingsProvider>();
        builder.Services.AddScoped<RasGateAdministrationService>();
        builder.Services.ConfigureReverseProxy(builder.Configuration);
        builder.Services.AddRasHubApi();

        builder.Services.AddRasHubInfrastructure(builder.Configuration);

        builder.Services.AddRasHubBackgroundTasks(options =>
        {
            builder.Configuration
                .GetSection(BackgroundTaskEngineOptions.SectionName)
                .Bind(options);
        });

        builder.Services.AddScoped<
            IBackgroundTaskHandler<CheckRasGateStatusTask>,
            CheckRasGateStatusTaskHandler>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<SynchronizeClustersTask>,
            SynchronizeClustersTaskHandler>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<SynchronizeClusterTask>,
            SynchronizeClusterTaskHandler>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<RemoveClusterTask>,
            RemoveClusterTaskHandler>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<CreateClusterTask, Guid>,
            CreateClusterTaskHandler>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<UpdateClusterTask>,
            UpdateClusterTaskHandler>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<SynchronizeInfobasesTask>,
            SynchronizeInfobasesTaskHandler>();
        builder.Services.AddScoped<
            IBackgroundTaskHandler<SynchronizeInfobaseTask>,
            SynchronizeInfobaseTaskHandler>();

        builder.Services
            .AddOptions<RasGateMonitoringOptions>()
            .Bind(builder.Configuration.GetSection(
                RasGateMonitoringOptions.SectionName))
            .Validate(
                value => value.PollInterval >=
                         BackgroundTaskTimerLimits.MinimumPeriodicInterval &&
                         value.PollInterval <=
                         BackgroundTaskTimerLimits.MaximumTimerDuration,
                "RasGate monitoring poll interval is outside the supported timer range.")
            .Validate(
                value => value.OnlineThreshold >= value.PollInterval,
                "RasGate online threshold must not be shorter than the poll interval.")
            .Validate(
                value => value.RequestTimeout > TimeSpan.Zero &&
                         value.RequestTimeout <=
                         BackgroundTaskTimerLimits.MaximumTimerDuration,
                "RasGate monitoring request timeout is outside the supported timer range.")
            .ValidateOnStart();
        builder.Services.AddHostedService<RasGateMonitoringService>();

        builder.Services
            .AddHealthChecks()
            .AddDbContextCheck<RasHubDbContext>("database", tags: ["ready"]);

        var app = builder.Build();

        if (!await app.ApplyDatabaseStartupPolicyAsync())
            return;

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