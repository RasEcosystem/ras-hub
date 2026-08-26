using RasHub.Application.RasGates.Exceptions;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Deserialization;

public sealed class RacClusterOutputDeserializerResolver(
    IEnumerable<IRacClusterOutputDeserializer> deserializers)
{
    private readonly IReadOnlyList<IRacClusterOutputDeserializer> _deserializers =
        deserializers.ToArray();

    public IRacClusterOutputDeserializer Resolve(Version racVersion)
    {
        return RacAdapterSelector.SelectLatestCompatible(
            _deserializers,
            deserializer => deserializer.MinimumVersion,
            racVersion,
            () => new RasGateClientException(
                $"RAC version '{racVersion}' does not have a supported " +
                "cluster output format."),
            minimumVersion => new InvalidOperationException(
                "Multiple RAC cluster output deserializers handle versions " +
                $"from minimum version '{minimumVersion}'."));
    }
}
