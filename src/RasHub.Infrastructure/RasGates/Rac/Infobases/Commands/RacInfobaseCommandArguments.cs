namespace RasHub.Infrastructure.RasGates.Rac.Infobases.Commands;

internal static class RacInfobaseCommandArguments
{
    public static void AddCluster(
        ICollection<string> arguments,
        RacInfobaseQuery query)
    {
        arguments.Add($"--cluster={query.ClusterId:D}");

        if (query.ClusterUser is not null)
            arguments.Add($"--cluster-user={query.ClusterUser}");

        if (query.ClusterPassword is not null)
            arguments.Add($"--cluster-pwd={query.ClusterPassword}");
    }

    public static void Validate(RacInfobaseQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.ClusterId == Guid.Empty)
            throw new ArgumentException(
                "A non-empty cluster external ID is required.",
                nameof(query));

        if (query.InfobaseId == Guid.Empty)
            throw new ArgumentException(
                "A non-empty infobase external ID is required when provided.",
                nameof(query));

        if (query.ClusterPassword is not null && query.ClusterUser is null)
            throw new ArgumentException(
                "A cluster user is required when a cluster password is provided.",
                nameof(query));
    }
}
