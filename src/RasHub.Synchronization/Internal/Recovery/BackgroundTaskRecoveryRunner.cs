using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RasHub.Synchronization.Internal;

internal sealed class BackgroundTaskRecoveryRunner
{
    private readonly ISynchronizationEngine _engine;
    private readonly ILogger<BackgroundTaskRecoveryRunner> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public BackgroundTaskRecoveryRunner(
        IServiceScopeFactory scopeFactory,
        ISynchronizationEngine engine,
        ILogger<BackgroundTaskRecoveryRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _engine = engine;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var sources = scope.ServiceProvider
            .GetServices<IBackgroundTaskRecoverySource>();

        foreach (var source in sources)
            try
            {
                await source.RecoverAsync(_engine, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Background task recovery source {RecoverySource} failed",
                    source.GetType().FullName);
            }
    }
}