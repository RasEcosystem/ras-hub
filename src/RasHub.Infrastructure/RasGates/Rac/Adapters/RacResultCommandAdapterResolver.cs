namespace RasHub.Infrastructure.RasGates.Rac.Adapters;

public sealed class RacResultCommandAdapterResolver<TCommand, TResult>(
    IEnumerable<IRacResultCommandAdapter<TCommand, TResult>> adapters)
{
    private readonly IReadOnlyList<IRacResultCommandAdapter<TCommand, TResult>>
        _adapters = adapters.ToArray();

    public IRacResultCommandAdapter<TCommand, TResult> Resolve(
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
