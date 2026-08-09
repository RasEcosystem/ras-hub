using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Adapters;

public sealed class RacResourceAdapterResolverTests
{
    [Fact]
    public void Resolve_multiple_schema_versions_selects_highest_supported_schema()
    {
        var resolver = new RacResourceAdapterResolver<string>(
        [
            new StubAdapter(1, new Version(8, 3, 27)),
            new StubAdapter(2, new Version(8, 3, 27))
        ]);

        var adapter = resolver.Resolve(
            "clusters",
            "snapshot",
            new Version(8, 3, 27));

        Assert.Equal(2, adapter.SchemaVersion);
    }

    [Fact]
    public void GetCapabilities_supported_version_returns_each_schema()
    {
        IRacResourceAdapterDescriptor[] adapters =
        [
            new StubAdapter(1, new Version(8, 3, 27)),
            new StubAdapter(2, new Version(8, 3, 27)),
            new StubAdapter(3, new Version(8, 3, 28))
        ];
        var resolver = new RacCapabilityResolver(adapters);

        var capabilities = resolver.GetCapabilities(new Version(8, 3, 27));

        Assert.Equal([1, 2], capabilities.Select(item => item.SchemaVersion));
    }

    [Fact]
    public void Resolve_unknown_version_rejects_resource_before_execution()
    {
        var resolver = new RacResourceAdapterResolver<string>(
            [new StubAdapter(1, new Version(8, 3, 27))]);

        Assert.Throws<RasGateClientException>(() => resolver.Resolve(
            "clusters",
            "snapshot",
            new Version(8, 3, 28)));
    }

    private sealed class StubAdapter(int schemaVersion, Version version)
        : IRacResourceAdapter<string>
    {
        public string Resource => "clusters";

        public string Operation => "snapshot";

        public int SchemaVersion => schemaVersion;

        public bool Supports(Version racVersion)
        {
            return racVersion == version;
        }

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