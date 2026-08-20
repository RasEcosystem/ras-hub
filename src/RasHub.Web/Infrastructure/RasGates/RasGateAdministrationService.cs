using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Services;
using RasHub.Application.RasGates.Tasks.Status;
using RasHub.BackgroundTasks.Models;
using RasHub.Domain;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.Infrastructure.RasGates;

public sealed record RasGateEditorValues(
    string Name,
    string Url,
    int Port,
    string? ApiKey,
    bool IsActive);

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
    RasHubDbContext dbContext,
    RasGateQueries queries,
    RasGateRegistry rasGateRegistry,
    IRasGateEndpointFactory endpointFactory,
    InteractiveTaskRunner taskRunner,
    AuthenticationStateProvider authenticationStateProvider,
    IAuthorizationService authorizationService,
    ILogger<RasGateAdministrationService> logger)
{
    public async Task<IReadOnlyList<RasGateAdministrationItem>> GetItemsAsync(
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();
        return await queries.GetAdministrationItemsAsync(
            includeDeleted,
            cancellationToken);
    }

    public async Task<RasGateAdministrationResult> CreateAsync(
        RasGateEditorValues values,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        var validation = Validate(values, true);
        if (validation is not null)
            return RasGateAdministrationResult.Failure(validation);

        var rasGate = await rasGateRegistry.RegisterAsync(
            new RasGateRegistration(
                values.Name,
                values.Url,
                values.Port,
                values.ApiKey!,
                values.IsActive),
            cancellationToken);

        logger.LogInformation("Administrator registered RasGate {RasGateId}", rasGate.Id);
        return RasGateAdministrationResult.Success();
    }

    public async Task<RasGateAdministrationResult> UpdateAsync(
        Guid id,
        RasGateEditorValues values,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        var validation = Validate(values, false);
        if (validation is not null)
            return RasGateAdministrationResult.Failure(validation);

        RasGate? rasGate;
        try
        {
            rasGate = await rasGateRegistry.UpdateAsync(
                id,
                new RasGateRegistrationUpdate(
                    values.Name,
                    values.Url,
                    values.Port,
                    values.IsActive,
                    string.IsNullOrWhiteSpace(values.ApiKey)
                        ? null
                        : values.ApiKey),
                cancellationToken);
        }
        catch (RasGateApiKeyRequiredException)
        {
            return RasGateAdministrationResult.Failure(
                "A new API key is required for this endpoint.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasGateAdministrationResult.Failure(
                "The RasGate changed concurrently. Reload the list and try again.");
        }

        if (rasGate is null)
            return RasGateAdministrationResult.Failure("The RasGate no longer exists.");

        logger.LogInformation("Administrator updated RasGate {RasGateId}", id);
        return RasGateAdministrationResult.Success();
    }

    public async Task<RasGateAdministrationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        RasGate? rasGate;
        try
        {
            rasGate = await rasGateRegistry.UnregisterAsync(
                id,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasGateAdministrationResult.Failure(
                "The RasGate changed concurrently. Reload the list and try again.");
        }

        if (rasGate is null)
            return RasGateAdministrationResult.Failure("The RasGate no longer exists.");

        logger.LogInformation("Administrator deleted RasGate {RasGateId}", id);
        return RasGateAdministrationResult.Success();
    }

    public async Task<RasGateAdministrationResult> RestoreAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        var rasGate = await dbContext.RasGates
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (rasGate is null)
            return RasGateAdministrationResult.Failure("The RasGate no longer exists.");
        if (!rasGate.IsDeleted)
            return RasGateAdministrationResult.Failure("The RasGate is not deleted.");

        try
        {
            await rasGateRegistry.RestoreAsync(rasGate, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasGateAdministrationResult.Failure(
                "The RasGate changed concurrently. Reload the list and try again.");
        }

        logger.LogInformation("Administrator restored RasGate {RasGateId}", id);
        return RasGateAdministrationResult.Success();
    }

    public async Task<RasGateAdministrationResult> SynchronizeStatusAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        var activity = await queries.GetActivityAsync(id, cancellationToken);
        if (activity is null)
            return RasGateAdministrationResult.Failure("The RasGate no longer exists.");
        if (!activity.IsActive)
            return RasGateAdministrationResult.Failure(
                "Activate the RasGate before refreshing its status.");

        var execution = await taskRunner.RunAsync(
            new CheckRasGateStatusTask(id),
            RasGateTaskOptions.InteractiveStatusSynchronization(id),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateAdministrationResult.Failure(
                "The status refresh could not be scheduled. Try again shortly.");

        var result = execution.Result!;
        if (result.IsSucceeded)
            return RasGateAdministrationResult.Success();

        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return RasGateAdministrationResult.Failure("The status refresh was canceled.");

        if (result.Exception is RasGateNotFoundException)
            return RasGateAdministrationResult.Failure(
                "The RasGate no longer exists.");

        if (result.Exception is RasGateConfigurationChangedException)
            return RasGateAdministrationResult.Failure(
                "The RasGate configuration changed during the status refresh.");

        if (result.Exception is RasGateInactiveException)
            return RasGateAdministrationResult.Failure(
                "The RasGate was deactivated during the status refresh.");

        return RasGateAdministrationResult.Failure(
            "The RasGate did not return a valid status. Check its endpoint and credentials.");
    }

    private string? Validate(RasGateEditorValues values, bool apiKeyRequired)
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

        try
        {
            _ = endpointFactory.CreateBaseAddress(values.Url.Trim(), values.Port);
        }
        catch (RasGateEndpointValidationException)
        {
            return "Enter a valid HTTP or HTTPS RasGate endpoint.";
        }

        return null;
    }

    private async Task EnsureAuthorizedAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var result = await authorizationService.AuthorizeAsync(
            state.User,
            AppPolicies.ManageRasGates);

        if (!result.Succeeded)
            throw new UnauthorizedAccessException(
                "Administrator access is required to manage RasGates.");
    }
}
