using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain.Enums;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Clusters;

public sealed class RacClusterInsertV1AdapterTests
{
    private static readonly Guid CreatedClusterId =
        Guid.Parse("8f5a6128-c013-4cd0-bd93-f4fd924d64c1");

    private readonly RacClusterInsertV1Adapter _adapter = new(
        new RacKeyValueOutputDeserializer());

    [Fact]
    public void Create_command_with_all_settings_returns_expected_arguments()
    {
        var options = new RasClusterCreationOptions
        {
            Host = "localhost",
            Port = 1587,
            Name = "Новый кластер",
            ExpirationTimeoutSeconds = 60,
            LifetimeLimitSeconds = 120,
            MaxMemorySizeKb = 1024,
            MaxMemoryTimeLimitSeconds = 30,
            SecurityLevel = 1,
            SessionFaultToleranceLevel = 2,
            LoadBalancingMode = RasClusterLoadBalancingMode.Memory,
            ErrorsCountThresholdPercent = 10,
            KillProblemProcesses = true,
            AgentUser = "agent-admin",
            AgentPassword = "agent-secret"
        };

        Assert.Equal(
            [
                "cluster",
                "insert",
                "--host=localhost",
                "--port=1587",
                "--name=Новый кластер",
                "--expiration-timeout=60",
                "--lifetime-limit=120",
                "--max-memory-size=1024",
                "--max-memory-time-limit=30",
                "--security-level=1",
                "--session-fault-tolerance-level=2",
                "--load-balancing-mode=memory",
                "--errors-count-threshold=10",
                "--kill-problem-processes=yes",
                "--agent-user=agent-admin",
                "--agent-pwd=agent-secret"
            ],
            _adapter.CreateCommand(options));
    }

    [Fact]
    public void Parse_real_success_output_returns_cluster_id()
    {
        var result = _adapter.Parse(
            new Version(8, 3, 27, 2214),
            CreateExecution($"cluster : {CreatedClusterId:D}\r\n"),
            CreateOptions());

        Assert.Equal(CreatedClusterId, result);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void Parse_failed_execution_rejects_result(
        int exitCode,
        bool timedOut)
    {
        Assert.Throws<RasGateClientException>(() => _adapter.Parse(
            new Version(8, 3, 27, 2214),
            CreateExecution(
                "",
                exitCode,
                timedOut,
                "Запуск рабочего процесса не возможен из-за конфликта IP портов"),
            CreateOptions()));
    }

    [Fact]
    public void Parse_failed_execution_does_not_expose_RAC_error_output()
    {
        const string racError =
            "Запуск рабочего процесса не возможен из-за конфликта IP портов";

        var exception = Assert.Throws<RasGateClientException>(() =>
            _adapter.Parse(
                new Version(8, 3, 27, 2214),
                CreateExecution("", 1, standardError: racError),
                CreateOptions()));

        Assert.DoesNotContain(racError, exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cluster : not-a-guid")]
    [InlineData("cluster : 00000000-0000-0000-0000-000000000000")]
    [InlineData("cluster : 8f5a6128-c013-4cd0-bd93-f4fd924d64c1\nextra : value")]
    public void Parse_malformed_success_output_rejects_result(string output)
    {
        Assert.Throws<RasGateClientException>(() => _adapter.Parse(
            new Version(8, 3, 27, 2214),
            CreateExecution(output),
            CreateOptions()));
    }

    [Fact]
    public void MinimumVersion_V1_adapter_returns_baseline_version()
    {
        Assert.Equal(new Version(8, 3, 27, 2214), _adapter.MinimumVersion);
    }

    [Fact]
    public void Parse_version_above_previous_family_boundary_returns_cluster_id()
    {
        var result = _adapter.Parse(
            new Version(8, 4, 0, 0),
            CreateExecution($"cluster : {CreatedClusterId:D}\r\n"),
            CreateOptions());

        Assert.Equal(CreatedClusterId, result);
    }

    [Fact]
    public void Parse_version_below_minimum_rejects_result()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _adapter.Parse(
            new Version(8, 3, 27, 2213),
            CreateExecution($"cluster : {CreatedClusterId:D}\r\n"),
            CreateOptions()));
    }

    private static RasClusterCreationOptions CreateOptions()
    {
        return new RasClusterCreationOptions
        {
            Host = "localhost",
            Port = 1587
        };
    }

    private static RacExecutionResult CreateExecution(
        string standardOutput,
        int exitCode = 0,
        bool timedOut = false,
        string standardError = "")
    {
        return new RacExecutionResult
        {
            Outcome = timedOut
                ? RacExecutionOutcome.Unknown
                : exitCode == 0
                    ? RacExecutionOutcome.Succeeded
                    : RacExecutionOutcome.Failed,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            DurationMilliseconds = 1,
            TimedOut = timedOut
        };
    }
}