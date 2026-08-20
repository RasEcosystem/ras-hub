using System.Globalization;
using RasHub.Application.RasGates.Models;
using RasHub.Domain.Enums;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Commands;

internal static class RacClusterCommandArguments
{
    public static void AddMutableSettings(
        ICollection<string> arguments,
        RasClusterUpdateOptions options)
    {
        AddMutableSettings(
            arguments,
            options.Name,
            options.ExpirationTimeoutSeconds,
            options.LifetimeLimitSeconds,
            options.MaxMemorySizeKb,
            options.MaxMemoryTimeLimitSeconds,
            options.SecurityLevel,
            options.SessionFaultToleranceLevel,
            options.LoadBalancingMode,
            options.ErrorsCountThresholdPercent,
            options.KillProblemProcesses,
            options.AgentUser,
            options.AgentPassword);
    }

    public static void AddMutableSettings(
        ICollection<string> arguments,
        RasClusterCreationOptions options)
    {
        AddMutableSettings(
            arguments,
            options.Name,
            options.ExpirationTimeoutSeconds,
            options.LifetimeLimitSeconds,
            options.MaxMemorySizeKb,
            options.MaxMemoryTimeLimitSeconds,
            options.SecurityLevel,
            options.SessionFaultToleranceLevel,
            options.LoadBalancingMode,
            options.ErrorsCountThresholdPercent,
            options.KillProblemProcesses,
            options.AgentUser,
            options.AgentPassword);
    }

    private static void AddMutableSettings(
        ICollection<string> arguments,
        string? name,
        long? expirationTimeoutSeconds,
        long? lifetimeLimitSeconds,
        long? maxMemorySizeKb,
        long? maxMemoryTimeLimitSeconds,
        int? securityLevel,
        int? sessionFaultToleranceLevel,
        RasClusterLoadBalancingMode? loadBalancingMode,
        int? errorsCountThresholdPercent,
        bool? killProblemProcesses,
        string? agentUser,
        string? agentPassword)
    {
        Add(arguments, "name", name);
        Add(arguments, "expiration-timeout", expirationTimeoutSeconds);
        Add(arguments, "lifetime-limit", lifetimeLimitSeconds);
        Add(arguments, "max-memory-size", maxMemorySizeKb);
        Add(arguments, "max-memory-time-limit", maxMemoryTimeLimitSeconds);
        Add(arguments, "security-level", securityLevel);
        Add(arguments, "session-fault-tolerance-level", sessionFaultToleranceLevel);

        if (loadBalancingMode is not null)
            arguments.Add(
                $"--load-balancing-mode={ToRacValue(loadBalancingMode.Value)}");

        Add(arguments, "errors-count-threshold", errorsCountThresholdPercent);

        if (killProblemProcesses is not null)
            arguments.Add(
                $"--kill-problem-processes={(killProblemProcesses.Value ? "yes" : "no")}");

        Add(arguments, "agent-user", agentUser);
        Add(arguments, "agent-pwd", agentPassword);
    }

    private static void Add(
        ICollection<string> arguments,
        string name,
        string? value)
    {
        if (value is not null)
            arguments.Add($"--{name}={value}");
    }

    private static void Add<T>(
        ICollection<string> arguments,
        string name,
        T? value)
        where T : struct, IFormattable
    {
        if (value is not null)
            arguments.Add(
                $"--{name}={value.Value.ToString(null, CultureInfo.InvariantCulture)}");
    }

    private static string ToRacValue(RasClusterLoadBalancingMode value)
    {
        return value switch
        {
            RasClusterLoadBalancingMode.Performance => "performance",
            RasClusterLoadBalancingMode.Memory => "memory",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }
}