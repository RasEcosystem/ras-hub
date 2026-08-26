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
        return RacAdapterSelector.Resolve(
            _adapters,
            resource,
            operation,
            racVersion);
    }
}
