using RasHub.Domain.Abstractions;
using RasHub.Domain.Enums;

namespace RasHub.Domain;

public sealed class RasCluster : IEntity, IAuditable, ISoftDeletable
{
    public Guid RasGateId { get; set; }

    public Guid ExternalId { get; set; }

    public required string Name { get; set; }

    public required string Host { get; set; }

    public int Port { get; set; }

    public long ExpirationTimeoutSeconds { get; set; }

    public long LifetimeLimitSeconds { get; set; }

    public long MaxMemorySizeKb { get; set; }

    public long MaxMemoryTimeLimitSeconds { get; set; }

    public int SecurityLevel { get; set; }

    public int SessionFaultToleranceLevel { get; set; }

    public RasClusterLoadBalancingMode LoadBalancingMode { get; set; }

    public int ErrorsCountThresholdPercent { get; set; }

    public bool KillProblemProcesses { get; set; }

    public bool? KillByMemoryWithDump { get; set; }

    public bool? AllowAccessRightAuditEventsRecording { get; set; }

    public long? PingPeriod { get; set; }

    public long? PingTimeout { get; set; }

    public string? RestartSchedule { get; set; }

    public DateTime ObservedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid Id { get; } = Guid.NewGuid();

    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
