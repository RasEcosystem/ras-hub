using RasHub.Domain;
using RasHub.Domain.Enums;

namespace RasHub.Infrastructure.IntegrationTests.Database;

internal static class RasClusterTestData
{
    public static RasCluster Create(
        Guid rasGateId,
        Guid? externalId = null,
        int port = 1541,
        string name = "Main cluster")
    {
        return new RasCluster
        {
            RasGateId = rasGateId,
            ExternalId = externalId ?? Guid.NewGuid(),
            Name = name,
            Host = "cluster.example.test",
            Port = port,
            ExpirationTimeoutSeconds = 60,
            LifetimeLimitSeconds = 86_400,
            MaxMemorySizeKb = 4_194_304,
            MaxMemoryTimeLimitSeconds = 300,
            SecurityLevel = 1,
            SessionFaultToleranceLevel = 2,
            LoadBalancingMode = RasClusterLoadBalancingMode.Memory,
            ErrorsCountThresholdPercent = 10,
            KillProblemProcesses = true,
            KillByMemoryWithDump = true,
            AllowAccessRightAuditEventsRecording = true,
            PingPeriod = 5,
            PingTimeout = 15,
            RestartSchedule = "0 3 * * *",
            ObservedAt = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc)
        };
    }
}