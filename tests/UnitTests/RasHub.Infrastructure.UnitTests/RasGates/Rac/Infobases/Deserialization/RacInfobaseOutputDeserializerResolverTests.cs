using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Infobases.Deserialization;

public sealed class RacInfobaseOutputDeserializerResolverTests
{
    [Fact]
    public void Resolve_multiple_formats_selects_latest_compatible_deserializer()
    {
        var resolver = new RacInfobaseOutputDeserializerResolver(
        [
            new StubDeserializer(1, new Version(8, 3, 27, 2214)),
            new StubDeserializer(2, new Version(8, 4, 0, 0))
        ]);

        var deserializer = resolver.Resolve(new Version(8, 5, 0, 0));

        Assert.Equal(2, deserializer.SchemaVersion);
    }

    [Fact]
    public void Resolve_version_below_earliest_format_rejects_output()
    {
        var resolver = new RacInfobaseOutputDeserializerResolver(
        [
            new StubDeserializer(1, new Version(8, 3, 27, 2214))
        ]);

        Assert.Throws<RasGateClientException>(() =>
            resolver.Resolve(new Version(8, 3, 26, 0)));
    }

    private sealed class StubDeserializer(
        int schemaVersion,
        Version minimumVersion)
        : IRacInfobaseOutputDeserializer
    {
        public int SchemaVersion { get; } = schemaVersion;

        public Version MinimumVersion { get; } = minimumVersion;

        public IReadOnlyList<RasInfobaseSnapshot> Deserialize(
            string standardOutput)
        {
            return [];
        }
    }
}
