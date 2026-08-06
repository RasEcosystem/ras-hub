namespace RasHub.Synchronization;

public interface IBackgroundTaskRecoverySource
{
    Task RecoverAsync(
        ISynchronizationEngine engine,
        CancellationToken cancellationToken);
}