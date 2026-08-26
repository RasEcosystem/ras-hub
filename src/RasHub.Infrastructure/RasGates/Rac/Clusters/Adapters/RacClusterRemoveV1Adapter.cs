using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Commands;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Adapters;

public sealed class RacClusterRemoveV1Adapter
    : IRacCommandAdapter<RemoveRasClusterCommand>
{
    public string Resource => "clusters";

    public string Operation => "remove";

    public int SchemaVersion => 1;

    public Version MinimumVersion { get; } = new(8, 3, 27, 2214);

    public IReadOnlyList<string> CreateCommand(RemoveRasClusterCommand command)
    {
        ValidateCommand(command);
        var arguments = new List<string> { "cluster", "remove", $"--cluster={command.ClusterId:D}" };

        if (command.ClusterUser is not null)
            arguments.Add($"--cluster-user={command.ClusterUser}");

        if (command.ClusterPassword is not null)
            arguments.Add($"--cluster-pwd={command.ClusterPassword}");

        return arguments;
    }

    public void Validate(
        Version racVersion,
        RacExecutionResult execution,
        RemoveRasClusterCommand command)
    {
        ValidateCommand(command);

        RacExecutionGuard.EnsureSucceeded(
            racVersion,
            MinimumVersion,
            execution,
            "cluster remove");
    }

    private static void ValidateCommand(RemoveRasClusterCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ClusterId == Guid.Empty)
            throw new ArgumentException(
                "A non-empty cluster external ID is required.",
                nameof(command));

        if (command.ClusterPassword is not null &&
            command.ClusterUser is null)
            throw new ArgumentException(
                "A cluster user is required when a cluster password is provided.",
                nameof(command));
    }
}
