using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Commands;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Adapters;

public sealed class RacClusterUpdateV1Adapter
    : IRacCommandAdapter<UpdateRasClusterCommand>
{
    public string Resource => "clusters";

    public string Operation => "update";

    public int SchemaVersion => 1;

    public Version MinimumVersion { get; } = new(8, 3, 27, 2214);

    public IReadOnlyList<string> CreateCommand(UpdateRasClusterCommand command)
    {
        ValidateCommand(command);
        var arguments = new List<string>
        {
            "cluster",
            "update",
            $"--cluster={command.ClusterId:D}"
        };
        RacClusterCommandArguments.AddMutableSettings(arguments, command.Options);
        return arguments;
    }

    public void Validate(
        Version racVersion,
        RacExecutionResult execution,
        UpdateRasClusterCommand command)
    {
        ValidateCommand(command);

        RacExecutionGuard.EnsureSucceeded(
            racVersion,
            MinimumVersion,
            execution,
            "cluster update");
    }

    private static void ValidateCommand(UpdateRasClusterCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ClusterId == Guid.Empty)
            throw new ArgumentException(
                "A non-empty cluster external ID is required.",
                nameof(command));

        ArgumentNullException.ThrowIfNull(command.Options);

        if (command.Options.AgentPassword is not null &&
            command.Options.AgentUser is null)
            throw new ArgumentException(
                "An agent user is required when an agent password is provided.",
                nameof(command));
    }
}