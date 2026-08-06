using Microsoft.Extensions.DependencyInjection;

namespace RasHub.Synchronization.Internal;

internal sealed class BackgroundTaskDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;

    public BackgroundTaskDispatcher(
        IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _scopeFactory = scopeFactory;
    }

    public async Task ExecuteAsync(
        BackgroundTaskExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        await using var scope =
            _scopeFactory.CreateAsyncScope();

        await execution.Invoker.InvokeAsync(
            scope.ServiceProvider,
            execution.BackgroundTask,
            cancellationToken);
    }
}