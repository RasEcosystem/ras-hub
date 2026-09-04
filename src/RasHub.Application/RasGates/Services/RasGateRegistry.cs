using RasHub.Application.Interfaces;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;

namespace RasHub.Application.RasGates.Services;

public sealed class RasGateRegistry(
    IRepository<RasGate> repository,
    IRasGateEndpointFactory endpointFactory,
    IUnitOfWork unitOfWork)
{
    public async Task<RasGate> RegisterAsync(
        RasGateRegistration registration,
        CancellationToken cancellationToken)
    {
        var name = registration.Name.Trim();
        var url = registration.Url.Trim();

        _ = endpointFactory.CreateBaseAddress(url, registration.Port);

        var rasGate = new RasGate
        {
            Name = name,
            Url = url,
            Port = registration.Port,
            ApiKey = registration.ApiKey,
            IsActive = registration.IsActive
        };

        await repository.AddAsync(rasGate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return rasGate;
    }

    public async Task<RasGate?> UpdateAsync(
        Guid rasGateId,
        RasGateRegistrationUpdate update,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdAsync(
            rasGateId,
            cancellationToken);

        if (rasGate is null)
            return null;

        if (rasGate.ConfigurationRevision != update.ExpectedConfigurationRevision)
            throw new RasGateRevisionConflictException(rasGateId);

        var name = update.Name.Trim();
        var url = update.Url.Trim();
        var endpointChanged =
            !string.Equals(rasGate.Url, url, StringComparison.Ordinal) ||
            rasGate.Port != update.Port;

        if (endpointChanged && update.ApiKey is null)
            throw new RasGateApiKeyRequiredException();

        _ = endpointFactory.CreateBaseAddress(url, update.Port);

        rasGate.Name = name;
        rasGate.Url = url;
        rasGate.Port = update.Port;

        if (update.ApiKey is not null)
            rasGate.ApiKey = update.ApiKey;

        if (rasGate.IsActive != update.IsActive)
        {
            rasGate.IsActive = update.IsActive;

            if (update.IsActive)
                rasGate.LastSeenAt = null;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return rasGate;
    }

    public async Task<RasGate?> UnregisterAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdAsync(
            rasGateId,
            cancellationToken);

        if (rasGate is null)
            return null;

        repository.Remove(rasGate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return rasGate;
    }

    public async Task<RasGate?> RestoreAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdIncludingDeletedAsync(
            rasGateId,
            cancellationToken);

        if (rasGate is null)
            return null;

        if (!rasGate.IsDeleted)
            throw new RasGateNotDeletedException(rasGateId);

        rasGate.IsDeleted = false;
        rasGate.DeletedAt = null;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return rasGate;
    }
}
