using RasHub.Application.Interfaces;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Domain;

namespace RasHub.Application.RasEndpoints.Services;

public sealed class RasEndpointRegistry(
    IRepository<RasEndpoint> repository,
    IRepository<RasGate> gateRepository,
    IRasClusterSnapshotStore snapshotStore,
    IUnitOfWork unitOfWork)
{
    public async Task<RasEndpoint> RegisterAsync(
        RasEndpointRegistration registration,
        CancellationToken cancellationToken)
    {
        await EnsureGateExistsAsync(
            registration.RasGateId,
            cancellationToken);
        var address = RasEndpointAddress.Create(
            registration.Host,
            registration.Port);
        var rasEndpoint = new RasEndpoint
        {
            Name = registration.Name.Trim(),
            RasGateId = registration.RasGateId,
            Host = address.Host,
            Port = address.Port,
            IsActive = registration.IsActive
        };

        await repository.AddAsync(rasEndpoint, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return rasEndpoint;
    }

    public async Task<RasEndpoint?> UpdateAsync(
        Guid rasEndpointId,
        RasEndpointRegistrationUpdate update,
        CancellationToken cancellationToken)
    {
        var rasEndpoint = await repository.GetByIdAsync(
            rasEndpointId,
            cancellationToken);

        if (rasEndpoint is null)
            return null;

        if (rasEndpoint.ConfigurationRevision !=
            update.ExpectedConfigurationRevision)
            throw new RasEndpointRevisionConflictException(rasEndpointId);

        await EnsureGateExistsAsync(update.RasGateId, cancellationToken);
        var address = RasEndpointAddress.Create(update.Host, update.Port);
        var remoteIdentityChanged =
            !string.Equals(
                rasEndpoint.Host,
                address.Host,
                StringComparison.Ordinal) ||
            rasEndpoint.Port != address.Port;
        var deactivated = !update.IsActive && rasEndpoint.IsActive;

        rasEndpoint.Name = update.Name.Trim();
        rasEndpoint.RasGateId = update.RasGateId;
        rasEndpoint.Host = address.Host;
        rasEndpoint.Port = address.Port;
        rasEndpoint.IsActive = update.IsActive;

        if (remoteIdentityChanged || deactivated)
            await snapshotStore.InvalidateAsync(
                rasEndpointId,
                cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return rasEndpoint;
    }

    public async Task<RasEndpoint?> UnregisterAsync(
        Guid rasEndpointId,
        CancellationToken cancellationToken)
    {
        var rasEndpoint = await repository.GetByIdAsync(
            rasEndpointId,
            cancellationToken);

        if (rasEndpoint is null)
            return null;

        await snapshotStore.InvalidateAsync(
            rasEndpointId,
            cancellationToken);
        repository.Remove(rasEndpoint);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return rasEndpoint;
    }

    public async Task RestoreAsync(
        RasEndpoint rasEndpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rasEndpoint);

        if (!rasEndpoint.IsDeleted)
            throw new InvalidOperationException(
                $"RAS endpoint '{rasEndpoint.Id}' is not deleted.");

        await snapshotStore.InvalidateAsync(
            rasEndpoint.Id,
            cancellationToken);
        rasEndpoint.IsDeleted = false;
        rasEndpoint.DeletedAt = null;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureGateExistsAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        if (await gateRepository.GetByIdAsync(
                rasGateId,
                cancellationToken) is null)
            throw new RasGateNotFoundException(rasGateId);
    }
}
