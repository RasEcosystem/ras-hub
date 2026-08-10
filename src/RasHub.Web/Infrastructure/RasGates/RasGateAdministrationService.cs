using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Tasks;
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
    public static RasGateAdministrationResult Success() => new(true, null);

    public static RasGateAdministrationResult Failure(string error) =>
        new(false, error);
}

public sealed class RasGateAdministrationService(
    RasHubDbContext dbContext,
    RasGateQueries queries,
    IRepository<RasGate> repository,
    IRasClusterSnapshotStore snapshotStore,
    IRasGateEndpointFactory endpointFactory,
    IUnitOfWork unitOfWork,
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

        var validation = Validate(values, apiKeyRequired: true);
        if (validation is not null)
            return RasGateAdministrationResult.Failure(validation);

        var rasGate = new RasGate
        {
            Name = values.Name.Trim(),
            Url = values.Url.Trim(),
            Port = values.Port,
            ApiKey = values.ApiKey!,
            IsActive = values.IsActive
        };

        await repository.AddAsync(rasGate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Administrator registered RasGate {RasGateId}", rasGate.Id);
        return RasGateAdministrationResult.Success();
    }

    public async Task<RasGateAdministrationResult> UpdateAsync(
        Guid id,
        RasGateEditorValues values,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        var rasGate = await repository.GetByIdAsync(id, cancellationToken);
        if (rasGate is null)
            return RasGateAdministrationResult.Failure("The RasGate no longer exists.");

        var normalizedUrl = values.Url.Trim();
        var endpointChanged =
            !string.Equals(rasGate.Url, normalizedUrl, StringComparison.Ordinal) ||
            rasGate.Port != values.Port;
        var validation = Validate(values, endpointChanged);
        if (validation is not null)
            return RasGateAdministrationResult.Failure(validation);

        var apiKeyChanged = !string.IsNullOrWhiteSpace(values.ApiKey) &&
                            !string.Equals(
                                rasGate.ApiKey,
                                values.ApiKey,
                                StringComparison.Ordinal);
        var remoteIdentityChanged = endpointChanged || apiKeyChanged;
        var deactivated = !values.IsActive && rasGate.IsActive;

        rasGate.Name = values.Name.Trim();
        rasGate.Url = normalizedUrl;
        rasGate.Port = values.Port;

        if (!string.IsNullOrWhiteSpace(values.ApiKey))
            rasGate.ApiKey = values.ApiKey;

        if (rasGate.IsActive != values.IsActive)
        {
            rasGate.IsActive = values.IsActive;

            if (values.IsActive)
                rasGate.LastSeenAt = null;
        }

        if (remoteIdentityChanged || deactivated)
        {
            rasGate.InstanceName = null;
            rasGate.Version = null;
            rasGate.StatusObservedAt = null;
            rasGate.LastSeenAt = null;
            await snapshotStore.InvalidateAsync(id, cancellationToken);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasGateAdministrationResult.Failure(
                "The RasGate changed concurrently. Reload the list and try again.");
        }

        logger.LogInformation("Administrator updated RasGate {RasGateId}", id);
        return RasGateAdministrationResult.Success();
    }

    public async Task<RasGateAdministrationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync();

        var rasGate = await repository.GetByIdAsync(id, cancellationToken);
        if (rasGate is null)
            return RasGateAdministrationResult.Failure("The RasGate no longer exists.");

        repository.Remove(rasGate);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasGateAdministrationResult.Failure(
                "The RasGate changed concurrently. Reload the list and try again.");
        }

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

        rasGate.IsDeleted = false;
        rasGate.DeletedAt = null;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
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
