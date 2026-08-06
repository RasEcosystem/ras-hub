namespace RasHub.Synchronization;

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