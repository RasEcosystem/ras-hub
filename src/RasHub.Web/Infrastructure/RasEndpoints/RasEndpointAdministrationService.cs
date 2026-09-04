using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Domain;
using RasHub.Infrastructure.Database;
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
    RasHubDbContext dbContext,
    RasEndpointQueries queries,
    RasGateQueries gateQueries,
    RasEndpointRegistry rasEndpointRegistry,
    AuthenticationStateProvider authenticationStateProvider,
    IAuthorizationService authorizationService,
    ILogger<RasEndpointAdministrationService> logger)
{
    public async Task<IReadOnlyList<RasEndpointAdministrationItem>>
        GetItemsAsync(
            bool includeDeleted,
            CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();
        return await queries.GetAdministrationItemsAsync(
            includeDeleted,
            cancellationToken);
    }

    public async Task<IReadOnlyList<RasEndpointGateOption>>
        GetGateOptionsAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();
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
        await EnsureAuthorizedAsync();

        var validation = Validate(values);
        if (validation is not null)
            return RasEndpointAdministrationResult.Failure(validation);

        RasEndpoint rasEndpoint;
        try
        {
            rasEndpoint = await rasEndpointRegistry.RegisterAsync(
                new RasEndpointRegistration(
                    values.Name,
                    values.RasGateId,
                    values.Host,
                    values.Port,
                    values.IsActive),
                cancellationToken);
        }
        catch (RasGateNotFoundException)
        {
            return RasEndpointAdministrationResult.Failure(
                "The selected RasGate no longer exists.");
        }

        logger.LogInformation(
            "Administrator registered RAS endpoint {RasEndpointId}",
            rasEndpoint.Id);
        return RasEndpointAdministrationResult.Success();
    }

    public async Task<RasEndpointAdministrationResult> UpdateAsync(
        Guid id,
        long expectedConfigurationRevision,
        RasEndpointEditorValues values,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        var validation = Validate(values);
        if (validation is not null)
            return RasEndpointAdministrationResult.Failure(validation);

        RasEndpoint? rasEndpoint;
        try
        {
            rasEndpoint = await rasEndpointRegistry.UpdateAsync(
                id,
                new RasEndpointRegistrationUpdate(
                    values.Name,
                    values.RasGateId,
                    values.Host,
                    values.Port,
                    values.IsActive,
                    expectedConfigurationRevision),
                cancellationToken);
        }
        catch (RasEndpointRevisionConflictException)
        {
            return RasEndpointAdministrationResult.Failure(
                "The RAS endpoint changed concurrently. Reload the list and try again.");
        }
        catch (RasGateNotFoundException)
        {
            return RasEndpointAdministrationResult.Failure(
                "The selected RasGate no longer exists.");
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return RasEndpointAdministrationResult.Failure(
                "The RAS endpoint changed concurrently. Reload the list and try again.");
        }

        if (rasEndpoint is null)
            return RasEndpointAdministrationResult.Failure(
                "The RAS endpoint no longer exists.");

        logger.LogInformation(
            "Administrator updated RAS endpoint {RasEndpointId}",
            id);
        return RasEndpointAdministrationResult.Success();
    }

    public async Task<RasEndpointAdministrationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        RasEndpoint? rasEndpoint;
        try
        {
            rasEndpoint = await rasEndpointRegistry.UnregisterAsync(
                id,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return RasEndpointAdministrationResult.Failure(
                "The RAS endpoint changed concurrently. Reload the list and try again.");
        }

        if (rasEndpoint is null)
            return RasEndpointAdministrationResult.Failure(
                "The RAS endpoint no longer exists.");

        logger.LogInformation(
            "Administrator deleted RAS endpoint {RasEndpointId}",
            id);
        return RasEndpointAdministrationResult.Success();
    }

    public async Task<RasEndpointAdministrationResult> RestoreAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        var rasEndpoint = await dbContext.RasEndpoints
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                endpoint => endpoint.Id == id,
                cancellationToken);

        if (rasEndpoint is null)
            return RasEndpointAdministrationResult.Failure(
                "The RAS endpoint no longer exists.");
        if (!rasEndpoint.IsDeleted)
            return RasEndpointAdministrationResult.Failure(
                "The RAS endpoint is not deleted.");

        try
        {
            await rasEndpointRegistry.RestoreAsync(
                rasEndpoint,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return RasEndpointAdministrationResult.Failure(
                "The RAS endpoint changed concurrently. Reload the list and try again.");
        }

        logger.LogInformation(
            "Administrator restored RAS endpoint {RasEndpointId}",
            id);
        return RasEndpointAdministrationResult.Success();
    }

    private static string? Validate(RasEndpointEditorValues values)
    {
        if (string.IsNullOrWhiteSpace(values.Name))
            return "Name is required.";
        if (values.Name.Trim().Length > RasEndpoint.NameMaxLength)
            return $"Name cannot exceed {RasEndpoint.NameMaxLength} characters.";
        if (values.RasGateId == Guid.Empty)
            return "Select a RasGate.";

        try
        {
            _ = RasEndpointAddress.Create(values.Host, values.Port);
        }
        catch (RasEndpointAddressValidationException)
        {
            return "Enter a valid RAS host and port.";
        }

        return null;
    }

    private async Task EnsureAuthorizedAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var result = await authorizationService.AuthorizeAsync(
            state.User,
            AppPolicies.ManageRasEndpoints);

        if (!result.Succeeded)
            throw new UnauthorizedAccessException(
                "Administrator access is required to manage RAS endpoints.");
    }
}
