using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.BackgroundTasks.Internal.Recovery;

internal sealed class BackgroundTaskRecoveryRunner
{
    private readonly IBackgroundTaskEngine _engine;
    private readonly ILogger<BackgroundTaskRecoveryRunner> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public BackgroundTaskRecoveryRunner(
        IServiceScopeFactory scopeFactory,
        IBackgroundTaskEngine engine,
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
            .GetServices<IBackgroundTaskRecoverySource>()
            .ToArray();

        _logger.LogInformation(
            "Background task recovery started with {RecoverySourceCount} sources",
            sources.Length);

        foreach (var source in sources)
            try
            {
                await source.RecoverAsync(_engine, cancellationToken);

                _logger.LogInformation(
                    "Background task recovery source {RecoverySource} completed",
                    source.GetType().FullName);
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

        _logger.LogInformation("Background task recovery completed");
    }
}