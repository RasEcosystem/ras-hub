using RasHub.Application.RasGates.Models;

namespace RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;

public interface IRacInfobaseOutputDeserializer
{
    int SchemaVersion { get; }

    Version MinimumVersion { get; }

    IReadOnlyList<RasInfobaseSnapshot> Deserialize(string standardOutput);
}
