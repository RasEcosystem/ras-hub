using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.Infrastructure.RasGates;

public sealed record RasGateEditorValues(
    string Name,
    string Url,
    int Port,
    string? ApiKey,
    bool IsActive)
{
    public override string ToString()
    {
        return nameof(RasGateEditorValues);
    }
}

public sealed record RasGateAdministrationResult(bool Succeeded, string? Error)
{
    public static RasGateAdministrationResult Success()
    {
        return new RasGateAdministrationResult(true, null);
    }

    public static RasGateAdministrationResult Failure(string error)
    {
        return new RasGateAdministrationResult(false, error);
    }
}

public sealed class RasGateAdministrationService(
    RasGateQueries queries,
    RasGateConfigurationAdministration configurationAdministration,
    RasGateStatusSynchronization statusSynchronization,
    AdministrationAuthorizationGuard authorizationGuard)
{
    public async Task<IReadOnlyList<RasGateAdministrationItem>> GetItemsAsync(
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasGateManagementAsync();
        return await queries.GetAdministrationItemsAsync(
            includeDeleted,
            cancellationToken);
    }

    public async Task<RasGateAdministrationResult> CreateAsync(
        RasGateEditorValues values,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasGateManagementAsync();
        return await configurationAdministration.CreateAsync(
            values,
            cancellationToken);
    }

    public async Task<RasGateAdministrationResult> UpdateAsync(
        Guid id,
        long expectedConfigurationRevision,
        RasGateEditorValues values,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasGateManagementAsync();
        return await configurationAdministration.UpdateAsync(
            id,
            expectedConfigurationRevision,
            values,
            cancellationToken);
    }

    public async Task<RasGateAdministrationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasGateManagementAsync();
        return await configurationAdministration.DeleteAsync(
            id,
            cancellationToken);
    }

    public async Task<RasGateAdministrationResult> RestoreAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasGateManagementAsync();
        return await configurationAdministration.RestoreAsync(
            id,
            cancellationToken);
    }

    public async Task<RasGateAdministrationResult> SynchronizeStatusAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await authorizationGuard.RequireRasGateManagementAsync();
        return await statusSynchronization.SynchronizeAsync(
            id,
            cancellationToken);
    }
}
