using RasHub.Application.RasGates.Exceptions;

namespace RasHub.Infrastructure.RasGates.Rac.Adapters;

public sealed class RacResourceAdapterResolver<T>(
    IEnumerable<IRacResourceAdapter<T>> adapters)
{
    private readonly IReadOnlyList<IRacResourceAdapter<T>> _adapters =
        adapters.ToArray();

    public IRacResourceAdapter<T> Resolve(
        string resource,
        string operation,
        Version racVersion)
    {
        var candidates = Find(resource, operation, racVersion)
            .OrderByDescending(adapter => adapter.SchemaVersion)
            .ToArray();

        if (candidates.Length == 0)
            throw new RasGateClientException(
                $"RAC version '{racVersion}' does not support " +
                $"'{resource}.{operation}'.");

        if (candidates.GroupBy(adapter => adapter.SchemaVersion)
            .Any(group => group.Count() > 1))
            throw new InvalidOperationException(
                $"Multiple RAC adapters handle '{resource}.{operation}' " +
                $"for version '{racVersion}'.");

        return candidates[0];
    }

    private IEnumerable<IRacResourceAdapter<T>> Find(
        string resource,
        string operation,
        Version racVersion)
    {
        return _adapters.Where(adapter =>
            string.Equals(
                adapter.Resource,
                resource,
                StringComparison.Ordinal) &&
            string.Equals(
                adapter.Operation,
                operation,
                StringComparison.Ordinal) &&
            adapter.Supports(racVersion));
    }
}