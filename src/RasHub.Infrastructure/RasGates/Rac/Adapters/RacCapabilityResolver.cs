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
            .Where(adapter => adapter.Supports(racVersion))
            .Select(adapter => new RasResourceCapability(
                adapter.Resource,
                adapter.Operation,
                adapter.SchemaVersion))
            .Distinct()
            .OrderBy(capability => capability.Resource)
            .ThenBy(capability => capability.Operation)
            .ThenBy(capability => capability.SchemaVersion)
            .ToArray();
    }
}