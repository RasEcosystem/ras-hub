using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Services;
using RasHub.Domain;

namespace RasHub.Web.Infrastructure.RasGates;

public sealed class RasGateConfigurationAdministration(
    RasGateRegistry rasGateRegistry,
    RasHubAdministrationMutationRunner mutationRunner,
    ILogger<RasGateAdministrationService> logger)
{
    public async Task<RasGateAdministrationResult> CreateAsync(
        RasGateEditorValues values,
        CancellationToken cancellationToken)
    {
        var validation = Validate(values, true);
        if (validation is not null)
            return RasGateAdministrationResult.Failure(validation);

        RasGate rasGate;
        try
        {
            rasGate = await mutationRunner.RunAsync(() =>
                rasGateRegistry.RegisterAsync(
                    new RasGateRegistration(
                        values.Name,
                        values.Url,
                        values.Port,
                        values.ApiKey!,
                        values.IsActive),
                    cancellationToken));
        }
        catch (RasGateEndpointValidationException)
        {
            return InvalidEndpoint();
        }

        logger.LogInformation(
            "Administrator registered RasGate {RasGateId}",
            rasGate.Id);
        return RasGateAdministrationResult.Success();
    }

    public async Task<RasGateAdministrationResult> UpdateAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        RasGateEditorValues values,
        CancellationToken cancellationToken)
    {
        var validation = Validate(values, false);
        if (validation is not null)
            return RasGateAdministrationResult.Failure(validation);

        RasGate? rasGate;
        try
        {
            rasGate = await mutationRunner.RunAsync(() =>
                rasGateRegistry.UpdateAsync(
                    rasGateId,
                    new RasGateRegistrationUpdate(
                        values.Name,
                        values.Url,
                        values.Port,
                        values.IsActive,
                        expectedConfigurationRevision,
                        string.IsNullOrWhiteSpace(values.ApiKey)
                            ? null
                            : values.ApiKey),
                    cancellationToken));
        }
        catch (RasGateApiKeyRequiredException)
        {
            return RasGateAdministrationResult.Failure(
                "A new API key is required for this endpoint.");
        }
        catch (RasGateRevisionConflictException)
        {
            return ConcurrentChange();
        }
        catch (RasGateEndpointValidationException)
        {
            return InvalidEndpoint();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }

        if (rasGate is null)
            return RasGateAdministrationResult.Failure(
                "The RasGate no longer exists.");

        logger.LogInformation(
            "Administrator updated RasGate {RasGateId}",
            rasGateId);
        return RasGateAdministrationResult.Success();
    }

    public async Task<RasGateAdministrationResult> DeleteAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        RasGate? rasGate;
        try
        {
            rasGate = await mutationRunner.RunAsync(() =>
                rasGateRegistry.UnregisterAsync(
                    rasGateId,
                    cancellationToken));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }

        if (rasGate is null)
            return RasGateAdministrationResult.Failure(
                "The RasGate no longer exists.");

        logger.LogInformation(
            "Administrator deleted RasGate {RasGateId}",
            rasGateId);
        return RasGateAdministrationResult.Success();
    }

    public async Task<RasGateAdministrationResult> RestoreAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        RasGate? rasGate;
        try
        {
            rasGate = await mutationRunner.RunAsync(() =>
                rasGateRegistry.RestoreAsync(
                    rasGateId,
                    cancellationToken));
        }
        catch (RasGateNotDeletedException)
        {
            return RasGateAdministrationResult.Failure(
                "The RasGate is not deleted.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange();
        }

        if (rasGate is null)
            return RasGateAdministrationResult.Failure(
                "The RasGate no longer exists.");

        logger.LogInformation(
            "Administrator restored RasGate {RasGateId}",
            rasGateId);
        return RasGateAdministrationResult.Success();
    }

    private static string? Validate(
        RasGateEditorValues values,
        bool apiKeyRequired)
    {
        if (string.IsNullOrWhiteSpace(values.Name))
            return "Name is required.";
        if (values.Name.Trim().Length > RasGate.NameMaxLength)
            return $"Name cannot exceed {RasGate.NameMaxLength} characters.";
        if (string.IsNullOrWhiteSpace(values.Url))
            return "URL is required.";
        if (values.Url.Trim().Length > RasGate.UrlMaxLength)
            return $"URL cannot exceed {RasGate.UrlMaxLength} characters.";
        if (values.Port is < 1 or > 65_535)
            return "Port must be between 1 and 65535.";
        if (apiKeyRequired && string.IsNullOrWhiteSpace(values.ApiKey))
            return "A new API key is required for this endpoint.";
        if (values.ApiKey?.Length > 512)
            return "API key cannot exceed 512 characters.";

        return null;
    }

    private static RasGateAdministrationResult InvalidEndpoint()
    {
        return RasGateAdministrationResult.Failure(
            "Enter a valid HTTP or HTTPS RasGate endpoint.");
    }

    private static RasGateAdministrationResult ConcurrentChange()
    {
        return RasGateAdministrationResult.Failure(
            "The RasGate changed concurrently. Reload the list and try again.");
    }
}
