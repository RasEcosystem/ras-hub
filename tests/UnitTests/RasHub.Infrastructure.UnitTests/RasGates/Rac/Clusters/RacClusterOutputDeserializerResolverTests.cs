using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Clusters;

public sealed class RacClusterOutputDeserializerResolverTests
{
    [Fact]
    public void Resolve_multiple_formats_selects_latest_compatible_deserializer()
    {
        var resolver = new RacClusterOutputDeserializerResolver(
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
        var resolver = new RacClusterOutputDeserializerResolver(
        [
            new StubDeserializer(1, new Version(8, 3, 27, 2214))
        ]);

        Assert.Throws<RasGateClientException>(() =>
            resolver.Resolve(new Version(8, 3, 27, 2213)));
    }

    [Fact]
    public void Resolve_duplicate_minimum_versions_rejects_ambiguous_formats()
    {
        var minimumVersion = new Version(8, 4, 0, 0);
        var resolver = new RacClusterOutputDeserializerResolver(
        [
            new StubDeserializer(1, minimumVersion),
            new StubDeserializer(2, minimumVersion)
        ]);

        Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve(new Version(8, 5, 0, 0)));
    }

    [Fact]
    public void Parse_new_output_format_uses_existing_operation_adapter()
    {
        var resolver = new RacClusterOutputDeserializerResolver(
        [
            new StubDeserializer(1, new Version(8, 3, 27, 2214)),
            new StubDeserializer(2, new Version(8, 4, 0, 0))
        ]);
        var adapter = new RacClusterSnapshotV1Adapter(resolver);
        var racVersion = new Version(8, 4, 1, 0);

        var snapshot = adapter.Parse(
            racVersion,
            new RacExecutionResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                DurationMilliseconds = 1,
                TimedOut = false
            });

        Assert.Equal(2, adapter.GetSchemaVersion(racVersion));
        Assert.Equal(2, snapshot.SchemaVersion);

        var capability = Assert.Single(
            new RacCapabilityResolver([adapter]).GetCapabilities(racVersion));
        Assert.Equal(2, capability.SchemaVersion);
    }

    private sealed class StubDeserializer(
        int schemaVersion,
        Version minimumVersion)
        : IRacClusterOutputDeserializer
    {
        public int SchemaVersion => schemaVersion;

        public Version MinimumVersion => minimumVersion;

        public IReadOnlyList<RasClusterSnapshot> Deserialize(string standardOutput)
        {
            return [];
        }
    }
}