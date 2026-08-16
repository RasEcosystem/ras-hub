using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using RasHub.BackgroundTasks;

namespace RasHub.Web.Infrastructure.Diagnostics;

internal static class OpenTelemetryConfigurationExtensions
{
    public static IServiceCollection AddRasHubOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredEndpoint = configuration["OpenTelemetry:MetricsEndpoint"];

        if (string.IsNullOrWhiteSpace(configuredEndpoint))
            return services;

        if (!Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException(
                "OpenTelemetry metrics endpoint must be an absolute HTTP or HTTPS URL.");

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("RasHub"))
            .WithMetrics(metrics => metrics
                .AddMeter(BackgroundTaskTelemetry.MeterName)
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                }));

        return services;
    }
}