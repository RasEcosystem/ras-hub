using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using RasHub.Contracts.Common;
using RasHub.Infrastructure;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Extensions;
using RasHub.Synchronization;
using RasHub.Web.Api;
using RasHub.Web.Api.Filters;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Authentication;
using RasHub.Web.Middlewares;
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

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();
        });

        builder.Services
            .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.Scheme,
                _ => { });

        builder.Services
            .AddOptions<RasHubOptions>()
            .BindConfiguration(RasHubOptions.SectionName)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "RasHub:ApiKey is required.")
            .ValidateOnStart();

        builder.Services.ConfigureReverseProxy(builder.Configuration);
        builder.Services.ConfigureApi();
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<ApiKeySecurityTransformer>();
            options.AddOperationTransformer<ApiKeySecurityTransformer>();
        });

        builder.Services.AddRasHubInfrastructure(builder.Configuration);

        builder.Services.AddRasHubSynchronization(options =>
        {
            builder.Configuration
                .GetSection(SynchronizationEngineOptions.SectionName)
                .Bind(options);
        });

        builder.Services
            .AddHealthChecks()
            .AddDbContextCheck<RasHubDbContext>("database", tags: ["ready"]);

        var app = builder.Build();

        var migrateOnly = string.Equals(app.Configuration["mode"], "migrate",
            StringComparison.OrdinalIgnoreCase);
        var applyMigrations = app.Configuration.GetValue<bool>("Database:ApplyMigrations");

        if (migrateOnly || applyMigrations)
            await app.Services.MigrateRasHubDatabaseAsync();

        if (migrateOnly)
            return;

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

        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add(new ConsumesAttribute("application/json"));
            options.Filters.Add(new ProducesAttribute("application/json"));
        });

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
            app.MapOpenApi();
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
                    .HideDeveloperTools()
                    .DisableMcp()
                    .DisableAgent();
            });
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

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
