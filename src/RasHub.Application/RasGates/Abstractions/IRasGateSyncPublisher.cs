using RasHub.Application.RasGates.Models;

namespace RasHub.Application.RasGates.Abstractions;

public interface IRasGateSyncPublisher
{
    Task<bool> TryPublishStatusAsync(
        Guid rasGateId,
        long expectedConfigurationRevision,
        RasGateStatus status,
        DateTime observedAt,
        CancellationToken cancellationToken);
}
