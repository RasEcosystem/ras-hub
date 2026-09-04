using RasHub.Infrastructure.Database.Queries;

namespace RasHub.Web.Api.RasEndpoints;

public sealed class ActiveRasEndpointLookup(RasEndpointQueries queries)
{
    public async Task<ActiveRasEndpointState> GetStateAsync(
        Guid rasEndpointId,
        CancellationToken cancellationToken)
    {
        var activity = await queries.GetActivityAsync(
            rasEndpointId,
            cancellationToken);

        if (activity is null)
            return ActiveRasEndpointState.NotFound;

        return activity.IsActive
            ? ActiveRasEndpointState.Active
            : ActiveRasEndpointState.Inactive;
    }
}

public enum ActiveRasEndpointState
{
    Active,
    NotFound,
    Inactive
}
