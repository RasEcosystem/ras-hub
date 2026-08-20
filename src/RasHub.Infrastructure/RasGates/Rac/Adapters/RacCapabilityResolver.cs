using RasHub.Application.RasGates.Models;

namespace RasHub.Infrastructure.RasGates.Rac.Adapters;

public sealed class RacCapabilityResolver(
    IEnumerable<IRacResourceAdapterDescriptor> adapters)
{
    private readonly IReadOnlyList<IRacResourceAdapterDescriptor> _adapters =
        adapters.ToArray();

    public IReadOnlyList<RasResourceCapability> GetCapabilities(
        Version racVersion)
    {
        return _adapters
            .Where(adapter => adapter.MinimumVersion <= racVersion)
            .GroupBy(adapter => (
                adapter.Resource,
                adapter.Operation))
            .Select(SelectAdapter)
            .Select(adapter => new RasResourceCapability(
                adapter.Resource,
                adapter.Operation,
                adapter.GetSchemaVersion(racVersion)))
            .OrderBy(capability => capability.Resource)
            .ThenBy(capability => capability.Operation)
            .ThenBy(capability => capability.SchemaVersion)
            .ToArray();
    }

    private static IRacResourceAdapterDescriptor SelectAdapter(
        IGrouping<
            (string Resource, string Operation),
            IRacResourceAdapterDescriptor> adapters)
    {
        var first = adapters.First();

        return RacAdapterSelector.SelectLatest(
            adapters,
            adapter => adapter.MinimumVersion,
            () => new InvalidOperationException(
                "A capability group cannot be empty."),
            minimumVersion => new InvalidOperationException(
                $"Multiple RAC adapters handle " +
                $"'{first.Resource}.{first.Operation}' " +
                $"from minimum version '{minimumVersion}'."));
    }
}
