namespace RasHub.Web.Infrastructure.RasGates;

public sealed class RasGateMonitoringOptions
{
    public const string SectionName = "RasGateMonitoring";

    public bool RunOnStartup { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan OnlineThreshold { get; set; } = TimeSpan.FromMinutes(3);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}