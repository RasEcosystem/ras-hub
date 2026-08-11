using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain.Enums;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Clusters;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Clusters;

public sealed class RacClusterUpdateV1AdapterTests
{
    private static readonly Guid ClusterId =
        Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7");

    private readonly RacClusterUpdateV1Adapter _adapter = new();

    [Fact]
    public void MinimumVersion_V1_adapter_returns_baseline_version()
    {
        Assert.Equal(new Version(8, 3, 27, 2214), _adapter.MinimumVersion);
    }

    [Fact]
    public void Create_command_with_settings_returns_expected_arguments()
    {
        var command = new UpdateRasClusterCommand(
            ClusterId,
            new RasClusterUpdateOptions
            {
                Name = "Обновленный кластер",
                LoadBalancingMode = RasClusterLoadBalancingMode.Performance,
                KillProblemProcesses = false,
                AgentUser = "agent-admin",
                AgentPassword = "agent-secret"
            });

        Assert.Equal(
            [
                "cluster",
                "update",
                $"--cluster={ClusterId:D}",
                "--name=Обновленный кластер",
                "--load-balancing-mode=performance",
                "--kill-problem-processes=no",
                "--agent-user=agent-admin",
                "--agent-pwd=agent-secret"
            ],
            _adapter.CreateCommand(command));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void Validate_failed_execution_rejects_result(
        int exitCode,
        bool timedOut)
    {
        Assert.Throws<RasGateClientException>(() => _adapter.Validate(
            new Version(8, 3, 27, 2214),
            CreateExecution(exitCode, timedOut),
            CreateCommand()));
    }

    [Fact]
    public void Validate_version_above_previous_family_boundary_accepts_result()
    {
        _adapter.Validate(
            new Version(8, 4, 0, 0),
            CreateExecution(),
            CreateCommand());
    }

    [Fact]
    public void Validate_version_below_minimum_rejects_result()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _adapter.Validate(
            new Version(8, 3, 27, 2213),
            CreateExecution(),
            CreateCommand()));
    }

    [Fact]
    public void Validate_failed_execution_does_not_expose_RAC_error_output()
    {
        const string racError = "agent-secret: conflict";

        var exception = Assert.Throws<RasGateClientException>(() =>
            _adapter.Validate(
                new Version(8, 3, 27, 2214),
                CreateExecution(1, standardError: racError),
                CreateCommand()));

        Assert.DoesNotContain(racError, exception.Message);
    }

    private static UpdateRasClusterCommand CreateCommand()
    {
        return new UpdateRasClusterCommand(
            ClusterId,
            new RasClusterUpdateOptions { Name = "Updated" });
    }

    private static RacExecutionResult CreateExecution(
        int exitCode = 0,
        bool timedOut = false,
        string standardError = "")
    {
        return new RacExecutionResult
        {
            ExitCode = exitCode,
            StandardOutput = "",
            StandardError = standardError,
            DurationMilliseconds = 1,
            TimedOut = timedOut
        };
    }
}