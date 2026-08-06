namespace RasHub.Synchronization.Abstractions;

/// <summary>
///     Reconstructs lost in-memory work from durable business state when the application starts.
/// </summary>
public interface IBackgroundTaskRecoverySource
{
    Task RecoverAsync(
        ISynchronizationEngine engine,
        CancellationToken cancellationToken);
}