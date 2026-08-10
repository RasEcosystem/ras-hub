using System.Net;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Controllers;
using RasHub.Web.Api;
using RasHub.Web.Authentication;
using RasHub.Web.Components;
using RasHub.Web.Middlewares;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

namespace RasHub.Web.Infrastructure.Configuration;

internal static class WebApplicationConfigurationExtensions
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

        app.UseRateLimiter();
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
