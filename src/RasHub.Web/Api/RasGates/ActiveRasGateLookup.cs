using RasHub.Infrastructure.Database.Queries;

namespace RasHub.Web.Api.RasGates;

public sealed class ActiveRasGateLookup(RasGateQueries queries)
{
    public async Task<ActiveRasGateState> GetStateAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var activity = await queries.GetActivityAsync(
            rasGateId,
            cancellationToken);

        if (activity is null)
            return ActiveRasGateState.NotFound;

        return activity.IsActive
            ? ActiveRasGateState.Active
            : ActiveRasGateState.Inactive;
    }
}

public enum ActiveRasGateState
{
    Active,
    NotFound,
    Inactive
}
