using Microsoft.Extensions.DependencyInjection;

namespace RasHub.BackgroundTasks.Internal.Execution;

/// <summary>
///     Creates a fresh DI scope for an attempt and dispatches the task to its typed handler.
/// </summary>
internal sealed class BackgroundTaskDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;

    public BackgroundTaskDispatcher(
        IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _scopeFactory = scopeFactory;
    }

    public async Task<object?> ExecuteAsync(
        BackgroundTaskExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        await using var scope =
            _scopeFactory.CreateAsyncScope();

        return await execution.Invoker.InvokeAsync(
            scope.ServiceProvider,
            execution.BackgroundTask,
            cancellationToken);
    }
}
