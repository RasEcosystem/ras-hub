using RasHub.Application.RasGates.Exceptions;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Clusters;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Clusters;

public sealed class RacClusterRemoveV1AdapterTests
{
    private static readonly Guid ClusterId =
        Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7");

    private readonly RacClusterRemoveV1Adapter _adapter = new();

    [Fact]
    public void Create_command_includes_requested_cluster_id()
    {
        Assert.Equal(
            ["cluster", "remove", $"--cluster={ClusterId:D}"],
            _adapter.CreateCommand(CreateCommand()));
    }

    [Fact]
    public void Create_command_includes_cluster_credentials_when_provided()
    {
        Assert.Equal(
            [
                "cluster",
                "remove",
                $"--cluster={ClusterId:D}",
                "--cluster-user=cluster-admin",
                "--cluster-pwd=cluster-secret"
            ],
            _adapter.CreateCommand(CreateCommand(
                "cluster-admin",
                "cluster-secret")));
    }

    [Fact]
    public void MinimumVersion_V1_adapter_returns_baseline_version()
    {
        Assert.Equal(new Version(8, 3, 27, 2214), _adapter.MinimumVersion);
    }

    [Fact]
    public void Validate_successful_execution_accepts_result()
    {
        _adapter.Validate(
            new Version(8, 3, 27, 2214),
            CreateExecution(),
            CreateCommand());
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
    public void Validate_failed_execution_does_not_expose_RAC_error_output()
    {
        var exception = Assert.Throws<RasGateClientException>(() =>
            _adapter.Validate(
                new Version(8, 3, 27, 2214),
                CreateExecution(1, standardError: "cluster-secret"),
                CreateCommand("cluster-admin", "cluster-secret")));

        Assert.DoesNotContain("cluster-secret", exception.Message);
    }

    [Fact]
    public void Create_command_without_cluster_id_rejects_request()
    {
        Assert.Throws<ArgumentException>(() =>
            _adapter.CreateCommand(new RemoveRasClusterCommand(
                Guid.Empty,
                null,
                null)));
    }

    [Fact]
    public void Create_command_with_password_but_no_user_rejects_request()
    {
        Assert.Throws<ArgumentException>(() =>
            _adapter.CreateCommand(CreateCommand(
                clusterPassword: "cluster-secret")));
    }

    private static RemoveRasClusterCommand CreateCommand(
        string? clusterUser = null,
        string? clusterPassword = null)
    {
        return new RemoveRasClusterCommand(
            ClusterId,
            clusterUser,
            clusterPassword);
    }

    private static RacExecutionResult CreateExecution(
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
            StandardOutput = "",
            StandardError = standardError,
            DurationMilliseconds = 1,
            TimedOut = timedOut
        };
    }
}