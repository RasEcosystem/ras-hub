using RasHub.Domain;
using RasHub.Domain.Enums;

namespace RasHub.Application.RasGates.Models;

public sealed record RasClusterSnapshot
{
    public required Guid ExternalId { get; init; }

    public required string Name { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    public required long ExpirationTimeoutSeconds { get; init; }

    public required long LifetimeLimitSeconds { get; init; }

    public required long MaxMemorySizeKb { get; init; }

    public required long MaxMemoryTimeLimitSeconds { get; init; }

    public required int SecurityLevel { get; init; }

    public required int SessionFaultToleranceLevel { get; init; }

    public required RasClusterLoadBalancingMode LoadBalancingMode { get; init; }

    public required int ErrorsCountThresholdPercent { get; init; }

    public required bool KillProblemProcesses { get; init; }

    public bool KillByMemoryWithDump { get; init; }

    public bool AllowAccessRightAuditEventsRecording { get; init; }

    public long PingPeriod { get; init; }

    public long PingTimeout { get; init; }

    public string RestartSchedule { get; init; } = string.Empty;
}