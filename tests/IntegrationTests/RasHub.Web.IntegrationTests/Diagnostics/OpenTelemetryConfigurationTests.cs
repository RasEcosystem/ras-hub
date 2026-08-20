using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace RasHub.Web.IntegrationTests.Diagnostics;

public sealed class OpenTelemetryConfigurationTests
{
    [Fact]
    public void Metrics_endpoint_configured_registers_meter_provider()
    {
        var services = ConfigureTelemetry(
            "http://127.0.0.1:4318/v1/metrics");

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetRequiredService<MeterProvider>());
    }

    [Theory]
    [InlineData("relative/metrics")]
    [InlineData("ftp://collector.example.test/metrics")]
    public void Metrics_endpoint_invalid_fails_during_registration(
        string endpoint)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ConfigureTelemetry(endpoint));

        Assert.Equal(
            "OpenTelemetry metrics endpoint must be an absolute HTTP or HTTPS URL.",
            exception.Message);
    }

    private static IServiceCollection ConfigureTelemetry(string endpoint)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenTelemetry:MetricsEndpoint"] = endpoint })
            .Build();
        var extensionType = typeof(Program).Assembly.GetType(
            "RasHub.Web.Infrastructure.Diagnostics." +
            "OpenTelemetryConfigurationExtensions",
            true)!;
        var method = extensionType.GetMethod(
                         "AddRasHubOpenTelemetry",
                         BindingFlags.Public | BindingFlags.Static) ??
                     throw new InvalidOperationException(
                         "OpenTelemetry registration method was not found.");

        try
        {
            return (IServiceCollection)method.Invoke(
                null,
                [services, configuration])!;
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}