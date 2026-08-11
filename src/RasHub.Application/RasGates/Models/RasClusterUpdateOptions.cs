using RasHub.Domain.Enums;

namespace RasHub.Application.RasGates.Models;

public sealed class RasClusterUpdateOptions
{
    public string? Name { get; init; }

    public long? ExpirationTimeoutSeconds { get; init; }

    public long? LifetimeLimitSeconds { get; init; }

    public long? MaxMemorySizeKb { get; init; }

    public long? MaxMemoryTimeLimitSeconds { get; init; }

    public int? SecurityLevel { get; init; }

    public int? SessionFaultToleranceLevel { get; init; }

    public RasClusterLoadBalancingMode? LoadBalancingMode { get; init; }

    public int? ErrorsCountThresholdPercent { get; init; }

    public bool? KillProblemProcesses { get; init; }

    public string? AgentUser { get; init; }

    public string? AgentPassword { get; init; }

    public override string ToString()
    {
        return nameof(RasClusterUpdateOptions);
    }
}