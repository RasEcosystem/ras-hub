namespace RasHub.Infrastructure.RasGates.Rac.Adapters;

public sealed class RacCommandAdapterResolver<TCommand>(
    IEnumerable<IRacCommandAdapter<TCommand>> adapters)
{
    private readonly IReadOnlyList<IRacCommandAdapter<TCommand>> _adapters =
        adapters.ToArray();

    public IRacCommandAdapter<TCommand> Resolve(
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
