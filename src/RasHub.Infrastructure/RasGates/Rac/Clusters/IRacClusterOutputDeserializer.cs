using RasHub.Application.RasGates.Models;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters;

public interface IRacClusterOutputDeserializer
{
    int SchemaVersion { get; }

    Version MinimumVersion { get; }

    IReadOnlyList<RasClusterSnapshot> Deserialize(string standardOutput);
}