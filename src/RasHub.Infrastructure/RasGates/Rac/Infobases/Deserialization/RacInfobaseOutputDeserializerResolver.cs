using RasHub.Application.RasGates.Exceptions;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;

public sealed class RacInfobaseOutputDeserializerResolver(
    IEnumerable<IRacInfobaseOutputDeserializer> deserializers)
{
    private readonly IReadOnlyList<IRacInfobaseOutputDeserializer>
        _deserializers = deserializers.ToArray();

    public IRacInfobaseOutputDeserializer Resolve(Version racVersion)
    {
        return RacAdapterSelector.SelectLatestCompatible(
            _deserializers,
            deserializer => deserializer.MinimumVersion,
            racVersion,
            () => new RasGateClientException(
                $"RAC version '{racVersion}' does not have a supported " +
                "infobase summary output format."),
            minimumVersion => new InvalidOperationException(
                "Multiple RAC infobase output deserializers handle versions " +
                $"from minimum version '{minimumVersion}'."));
    }
}
