namespace RasHub.Infrastructure.RasGates.Rac.Infobases.Commands;

public sealed class RacInfobaseQuery(
    Guid clusterId,
    Guid? infobaseId = null,
    string? clusterUser = null,
    string? clusterPassword = null)
{
    public Guid ClusterId { get; } = clusterId;

    public Guid? InfobaseId { get; } = infobaseId;

    public string? ClusterUser { get; } = clusterUser;

    public string? ClusterPassword { get; } = clusterPassword;

    public override string ToString()
    {
        return nameof(RacInfobaseQuery);
    }
}
