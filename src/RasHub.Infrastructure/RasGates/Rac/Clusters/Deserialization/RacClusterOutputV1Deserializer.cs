using System.Globalization;
using RasHub.Application.RasGates.Models;
using RasHub.Domain.Enums;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Deserialization;

public sealed class RacClusterOutputV1Deserializer(
    RacKeyValueOutputDeserializer keyValueDeserializer)
    : IRacClusterOutputDeserializer
{
    public int SchemaVersion => 1;

    public Version MinimumVersion { get; } = new(8, 3, 27, 2214);

    public IReadOnlyList<RasClusterSnapshot> Deserialize(string standardOutput)
    {
        var records = keyValueDeserializer.Deserialize(standardOutput);
        var clusters = records
            .Select(DeserializeRecord)
            .ToArray();
        var externalIds = new HashSet<Guid>();

        foreach (var cluster in clusters)
            if (!externalIds.Add(cluster.ExternalId))
                throw new RacOutputDeserializationException(
                    $"RAC output contains duplicate cluster '{cluster.ExternalId}'.");

        return clusters;
    }

    private static RasClusterSnapshot DeserializeRecord(RacKeyValueRecord record)
    {
        var externalId = ParseGuid(record, "cluster");
        var port = ParseInt32(record, "port");

        if (port is < 1 or > 65_535)
            throw InvalidValue("port");

        return new RasClusterSnapshot
        {
            ExternalId = externalId,
            Host = Unquote(GetRequiredValue(record, "host")),
            Port = port,
            Name = Unquote(GetRequiredValue(record, "name")),
            ExpirationTimeoutSeconds = ParseInt64(record, "expiration-timeout"),
            LifetimeLimitSeconds = ParseInt64(record, "lifetime-limit"),
            MaxMemorySizeKb = ParseInt64(record, "max-memory-size"),
            MaxMemoryTimeLimitSeconds = ParseInt64(record, "max-memory-time-limit"),
            SecurityLevel = ParseInt32(record, "security-level"),
            SessionFaultToleranceLevel = ParseInt32(
                record,
                "session-fault-tolerance-level"),
            LoadBalancingMode = ParseLoadBalancingMode(record),
            ErrorsCountThresholdPercent = ParseInt32(
                record,
                "errors-count-threshold"),
            KillProblemProcesses = ParseBoolean(record, "kill-problem-processes"),
            KillByMemoryWithDump = ParseOptionalBoolean(
                record,
                "kill-by-memory-with-dump"),
            AllowAccessRightAuditEventsRecording = ParseOptionalBoolean(
                record,
                "allow-access-right-audit-events-recording"),
            PingPeriod = ParseOptionalInt64(record, "ping-period"),
            PingTimeout = ParseOptionalInt64(record, "ping-timeout"),
            RestartSchedule = UnquoteOptional(
                GetOptionalValue(record, "restart-schedule"))
        };
    }

    private static Guid ParseGuid(RacKeyValueRecord record, string key)
    {
        if (Guid.TryParse(GetRequiredValue(record, key), out var value) &&
            value != Guid.Empty)
            return value;

        throw InvalidValue(key);
    }

    private static int ParseInt32(RacKeyValueRecord record, string key)
    {
        if (int.TryParse(
                GetRequiredValue(record, key),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
            return value;

        throw InvalidValue(key);
    }

    private static long ParseInt64(RacKeyValueRecord record, string key)
    {
        if (long.TryParse(
                GetRequiredValue(record, key),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
            return value;

        throw InvalidValue(key);
    }

    private static long? ParseOptionalInt64(RacKeyValueRecord record, string key)
    {
        return record.Values.ContainsKey(key)
            ? ParseInt64(record, key)
            : null;
    }

    private static bool ParseBoolean(RacKeyValueRecord record, string key)
    {
        return GetRequiredValue(record, key).ToLowerInvariant() switch
        {
            "1" or "yes" or "true" => true,
            "0" or "no" or "false" => false,
            _ => throw InvalidValue(key)
        };
    }

    private static bool? ParseOptionalBoolean(RacKeyValueRecord record, string key)
    {
        return record.Values.ContainsKey(key)
            ? ParseBoolean(record, key)
            : null;
    }

    private static RasClusterLoadBalancingMode ParseLoadBalancingMode(
        RacKeyValueRecord record)
    {
        const string key = "load-balancing-mode";

        return GetRequiredValue(record, key).ToLowerInvariant() switch
        {
            "performance" => RasClusterLoadBalancingMode.Performance,
            "memory" => RasClusterLoadBalancingMode.Memory,
            _ => throw InvalidValue(key)
        };
    }

    private static string GetRequiredValue(RacKeyValueRecord record, string key)
    {
        if (record.Values.TryGetValue(key, out var value))
            return value;

        throw new RacOutputDeserializationException(
            $"RAC output record does not contain required key '{key}'.");
    }

    private static string? GetOptionalValue(RacKeyValueRecord record, string key)
    {
        return record.Values.TryGetValue(key, out var value)
            ? value
            : null;
    }

    private static string Unquote(string value)
    {
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;
    }

    private static string? UnquoteOptional(string? value)
    {
        return value is null
            ? null
            : Unquote(value);
    }

    private static RacOutputDeserializationException InvalidValue(string key)
    {
        return new RacOutputDeserializationException(
            $"RAC output contains an invalid value for key '{key}'.");
    }
}
