using RasHub.Application.RasGates.Exceptions;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Adapters;

public sealed class RacResultCommandAdapterResolverTests
{
    [Fact]
    public void Resolve_multiple_minimum_versions_selects_latest_compatible_adapter()
    {
        var resolver = new RacResultCommandAdapterResolver<Guid, string>(
        [
            new StubAdapter(1, new Version(8, 3, 27, 2214)),
            new StubAdapter(2, new Version(8, 4, 0, 0))
        ]);

        var adapter = resolver.Resolve(
            "clusters",
            "insert",
            new Version(9, 0, 0, 0));

        Assert.Equal(2, adapter.SchemaVersion);
        Assert.Equal(new Version(8, 4, 0, 0), adapter.MinimumVersion);
    }

    [Fact]
    public void Resolve_version_below_earliest_minimum_rejects_resource()
    {
        var resolver = new RacResultCommandAdapterResolver<Guid, string>(
            [new StubAdapter(1, new Version(8, 3, 27, 2214))]);

        Assert.Throws<RasGateClientException>(() => resolver.Resolve(
            "clusters",
            "insert",
            new Version(8, 3, 27, 2213)));
    }

    private sealed class StubAdapter(
        int schemaVersion,
        Version minimumVersion)
        : IRacResultCommandAdapter<Guid, string>
    {
        public string Resource => "clusters";

        public string Operation => "insert";

        public int SchemaVersion => schemaVersion;

        public Version MinimumVersion => minimumVersion;

        public IReadOnlyList<string> CreateCommand(Guid command)
        {
            return ["cluster", "insert"];
        }

        public string Parse(
            Version racVersion,
            RacExecutionResult execution,
            Guid command)
        {
            return command.ToString("D");
        }
    }
}