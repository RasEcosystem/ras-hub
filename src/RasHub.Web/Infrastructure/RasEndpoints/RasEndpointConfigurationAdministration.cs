using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Domain;

namespace RasHub.Web.Infrastructure.RasEndpoints;

public sealed class RasEndpointConfigurationAdministration(
    RasEndpointRegistry rasEndpointRegistry,
    RasHubAdministrationMutationRunner mutationRunner,
    ILogger<RasEndpointAdministrationService> logger)
{
    public async Task<RasEndpointAdministrationResult> CreateAsync(
        RasEndpointEditorValues values,
        CancellationToken cancellationToken)
    {
        var validation = Validate(values);
        if (validation is not null)
            return RasEndpointAdministrationResult.Failure(validation);

        RasEndpoint rasEndpoint;
        try
        {
            rasEndpoint = await mutationRunner.RunAsync(() =>
                rasEndpointRegistry.RegisterAsync(
                    new RasEndpointRegistration(
                        values.Name,
                        values.RasGateId,
                        values.Host,
                        values.Port,
                        values.IsActive),
                    cancellationToken));
        }
        catch (RasGateNotFoundException)
        {
            return MissingGate();
        }

        logger.LogInformation(
            "Administrator registered RAS endpoint {RasEndpointId}",
            rasEndpoint.Id);
        return RasEndpointAdministrationResult.Success();
    }

    public async Task<RasEndpointAdministrationResult> UpdateAsync(
        Guid rasEndpointId,
        long expectedConfigurationRevision,
        RasEndpointEditorValues values,
        CancellationToken cancellationToken)
    {
        var validation = Validate(values);
        if (validation is not null)
            return RasEndpointAdministrationResult.Failure(validation);

        RasEndpoint? rasEndpoint;
        try
        {
            rasEndpoint = await mutationRunner.RunAsync(() =>
                rasEndpointRegistry.UpdateAsync(
                    rasEndpointId,
                    new RasEndpointRegistrationUpdate(
                        values.Name,
                        values.RasGateId,
                        values.Host,
                        values.Port,
                        values.IsActive,
                        expectedConfigurationRevision),
                    cancellationToken));
        }
        catch (RasEndpointRevisionConflictException)
        {
            return ConcurrentChange();
        }
        catch (RasGateNotFoundException)
        {
            return MissingGate();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }

        if (rasEndpoint is null)
            return MissingEndpoint();

        logger.LogInformation(
            "Administrator updated RAS endpoint {RasEndpointId}",
            rasEndpointId);
        return RasEndpointAdministrationResult.Success();
    }

    public async Task<RasEndpointAdministrationResult> DeleteAsync(
        Guid rasEndpointId,
        CancellationToken cancellationToken)
    {
        RasEndpoint? rasEndpoint;
        try
        {
            rasEndpoint = await mutationRunner.RunAsync(() =>
                rasEndpointRegistry.UnregisterAsync(
                    rasEndpointId,
                    cancellationToken));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }

        if (rasEndpoint is null)
            return MissingEndpoint();

        logger.LogInformation(
            "Administrator deleted RAS endpoint {RasEndpointId}",
            rasEndpointId);
        return RasEndpointAdministrationResult.Success();
    }

    public async Task<RasEndpointAdministrationResult> RestoreAsync(
        Guid rasEndpointId,
        CancellationToken cancellationToken)
    {
        RasEndpoint? rasEndpoint;
        try
        {
            rasEndpoint = await mutationRunner.RunAsync(() =>
                rasEndpointRegistry.RestoreAsync(
                    rasEndpointId,
                    cancellationToken));
        }
        catch (RasEndpointNotDeletedException)
        {
            return RasEndpointAdministrationResult.Failure(
                "The RAS endpoint is not deleted.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }

        if (rasEndpoint is null)
            return MissingEndpoint();

        logger.LogInformation(
            "Administrator restored RAS endpoint {RasEndpointId}",
            rasEndpointId);
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

    private static RasEndpointAdministrationResult MissingGate()
    {
        return RasEndpointAdministrationResult.Failure(
            "The selected RasGate no longer exists.");
    }

    private static RasEndpointAdministrationResult MissingEndpoint()
    {
        return RasEndpointAdministrationResult.Failure(
            "The RAS endpoint no longer exists.");
    }

    private static RasEndpointAdministrationResult ConcurrentChange()
    {
        return RasEndpointAdministrationResult.Failure(
            "The RAS endpoint changed concurrently. Reload the list and try again.");
    }
}
