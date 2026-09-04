using RasHub.Application.Interfaces;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Domain;

namespace RasHub.Application.RasEndpoints.Services;

public sealed class RasEndpointRegistry(
    IRepository<RasEndpoint> repository,
    IUnitOfWork unitOfWork)
{
    public async Task<RasEndpoint> RegisterAsync(
        RasEndpointRegistration registration,
        CancellationToken cancellationToken)
    {
        var address = RasEndpointAddress.Create(
            registration.Host,
            registration.Port);
        var rasEndpoint = new RasEndpoint
        {
            Name = registration.Name.Trim(),
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

        var address = RasEndpointAddress.Create(update.Host, update.Port);

        rasEndpoint.Name = update.Name.Trim();
        rasEndpoint.Host = address.Host;
        rasEndpoint.Port = address.Port;
        rasEndpoint.IsActive = update.IsActive;

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

        rasEndpoint.IsDeleted = false;
        rasEndpoint.DeletedAt = null;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
