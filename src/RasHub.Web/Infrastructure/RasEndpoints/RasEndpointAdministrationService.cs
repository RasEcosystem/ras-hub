using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.Infrastructure.RasEndpoints;

public sealed record RasEndpointEditorValues(
    string Name,
    Guid RasGateId,
    string Host,
    int Port,
    bool IsActive);

public sealed record RasEndpointGateOption(
    Guid Id,
    string Name,
    string Url,
    int Port,
    bool IsActive);

public sealed record RasEndpointAdministrationResult(
    bool Succeeded,
    string? Error)
{
    public static RasEndpointAdministrationResult Success()
    {
        return new RasEndpointAdministrationResult(true, null);
    }

    public static RasEndpointAdministrationResult Failure(string error)
    {
        return new RasEndpointAdministrationResult(false, error);
    }
}

public sealed class RasEndpointAdministrationService(
    RasEndpointQueries queries,
    RasGateQueries gateQueries,
    RasEndpointConfigurationAdministration configurationAdministration,
    AdministrationAuthorizationGuard authorizationGuard)
{
    public async Task<IReadOnlyList<RasEndpointAdministrationItem>>
        GetItemsAsync(
            bool includeDeleted,
            CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasEndpointManagementAsync();
        return await queries.GetAdministrationItemsAsync(
            includeDeleted,
            cancellationToken);
    }

    public async Task<IReadOnlyList<RasEndpointGateOption>>
        GetGateOptionsAsync(CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasEndpointManagementAsync();
        var gates = await gateQueries.GetAdministrationItemsAsync(
            false,
            cancellationToken);

        return gates
            .OrderByDescending(gate => gate.IsActive)
            .ThenBy(gate => gate.Name)
            .ThenBy(gate => gate.Id)
            .Select(gate => new RasEndpointGateOption(
                gate.Id,
                gate.Name,
                gate.Url,
                gate.Port,
                gate.IsActive))
            .ToArray();
    }

    public async Task<RasEndpointAdministrationResult> CreateAsync(
        RasEndpointEditorValues values,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasEndpointManagementAsync();
        return await configurationAdministration.CreateAsync(
            values,
            cancellationToken);
    }

    public async Task<RasEndpointAdministrationResult> UpdateAsync(
        Guid id,
        long expectedConfigurationRevision,
        RasEndpointEditorValues values,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasEndpointManagementAsync();
        return await configurationAdministration.UpdateAsync(
            id,
            expectedConfigurationRevision,
            values,
            cancellationToken);
    }

    public async Task<RasEndpointAdministrationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasEndpointManagementAsync();
        return await configurationAdministration.DeleteAsync(
            id,
            cancellationToken);
    }

    public async Task<RasEndpointAdministrationResult> RestoreAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasEndpointManagementAsync();
        return await configurationAdministration.RestoreAsync(
            id,
            cancellationToken);
    }
}
