namespace RasHub.Synchronization.Models;

/// <summary>
///     Identifies one execution and lets a caller wait for its terminal result without owning the work itself.
/// </summary>
public sealed class BackgroundTaskHandle
{
    private readonly Task<BackgroundTaskResult> _completion;

    internal BackgroundTaskHandle(
        Guid id,
        Task<BackgroundTaskResult> completion)
    {
        Id = id;
        _completion = completion;
    }

    public Guid Id { get; }

    public Task<BackgroundTaskResult> WaitAsync(
        CancellationToken cancellationToken = default)
    {
        return _completion.WaitAsync(cancellationToken);
    }
}