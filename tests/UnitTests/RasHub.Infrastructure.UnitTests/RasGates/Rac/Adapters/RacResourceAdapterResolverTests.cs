using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Adapters;

public sealed class RacResourceAdapterResolverTests
{
    [Fact]
    public void Resolve_multiple_minimum_versions_selects_latest_compatible_adapter()
    {
        var resolver = new RacResourceAdapterResolver<string>(
        [
            new StubAdapter(1, new Version(8, 3, 27, 2214)),
            new StubAdapter(2, new Version(8, 4, 0, 0))
        ]);

        var adapter = resolver.Resolve(
            "clusters",
            "snapshot",
            new Version(8, 5, 0, 0));

        Assert.Equal(2, adapter.SchemaVersion);
        Assert.Equal(new Version(8, 4, 0, 0), adapter.MinimumVersion);
    }

    [Fact]
    public void GetCapabilities_multiple_minimum_versions_returns_selected_schema()
    {
        IRacResourceAdapterDescriptor[] adapters =
        [
            new StubAdapter(1, new Version(8, 3, 27, 2214)),
            new StubAdapter(2, new Version(8, 4, 0, 0)),
            new StubAdapter(3, new Version(8, 6, 0, 0))
        ];
        var resolver = new RacCapabilityResolver(adapters);

        var capabilities = resolver.GetCapabilities(new Version(8, 5, 0, 0));

        var capability = Assert.Single(capabilities);
        Assert.Equal(2, capability.SchemaVersion);
    }

    [Fact]
    public void Resolve_version_below_earliest_minimum_rejects_resource()
    {
        var resolver = new RacResourceAdapterResolver<string>(
            [new StubAdapter(1, new Version(8, 3, 27, 2214))]);

        Assert.Throws<RasGateClientException>(() => resolver.Resolve(
            "clusters",
            "snapshot",
            new Version(8, 3, 27, 2213)));
    }

    [Fact]
    public void Resolve_duplicate_minimum_versions_rejects_ambiguous_adapters()
    {
        var minimumVersion = new Version(8, 3, 27, 2214);
        var resolver = new RacResourceAdapterResolver<string>(
        [
            new StubAdapter(1, minimumVersion),
            new StubAdapter(2, minimumVersion)
        ]);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            "clusters",
            "snapshot",
            new Version(8, 4, 0, 0)));
    }

    private sealed class StubAdapter(
        int schemaVersion,
        Version minimumVersion)
        : IRacResourceAdapter<string>
    {
        public string Resource => "clusters";

        public string Operation => "snapshot";

        public int SchemaVersion => schemaVersion;

        public Version MinimumVersion => minimumVersion;

        public IReadOnlyList<string> CreateCommand(Guid? externalId = null)
        {
            return ["cluster", "list"];
        }

        public RasResourceSnapshot<string> Parse(
            Version racVersion,
            RacExecutionResult execution,
            Guid? externalId = null)
        {
            return new RasResourceSnapshot<string>
            {
                SchemaVersion = SchemaVersion,
                SourceVersion = racVersion.ToString(),
                Completeness = SnapshotCompleteness.Complete,
                Items = []
            };
        }
    }
}
