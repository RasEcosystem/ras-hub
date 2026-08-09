using Microsoft.Extensions.Options;
using RasHub.Application.RasGates.Tasks;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Exceptions;
using RasHub.Synchronization.Models;

namespace RasHub.Web.Infrastructure.RasGates;

public sealed class RasGateMonitoringService(
    IServiceScopeFactory scopeFactory,
    ISynchronizationEngine synchronizationEngine,
    IOptions<RasGateMonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<RasGateMonitoringService> logger)
    : BackgroundService
{
    private RasGateMonitoringOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Options.RunOnStartup)
            await TryEnqueueStatusRefreshesAsync(stoppingToken);

        using var timer = new PeriodicTimer(Options.PollInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TryEnqueueStatusRefreshesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task TryEnqueueStatusRefreshesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await EnqueueStatusRefreshesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unable to schedule RasGate status monitoring");
        }
    }

    private async Task EnqueueStatusRefreshesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<RasGateQueries>();
        var rasGateIds = await queries.GetActiveIdsAsync(cancellationToken);

        foreach (var rasGateId in rasGateIds)
            try
            {
                synchronizationEngine.Enqueue(
                    new RefreshRasGateStatusTask(rasGateId),
                    new BackgroundTaskOptions
                    {
                        Queue = BackgroundTaskQueue.Synchronization,
                        MaxAttempts = 2,
                        RetryDelay = TimeSpan.FromSeconds(1),
                        Timeout = Options.RequestTimeout,
                        DeduplicationKey = $"ras-gate-status:{rasGateId}",
                        ConcurrencyKey = $"ras-gate:{rasGateId}"
                    });
            }
            catch (BackgroundTaskRejectedException exception)
            {
                logger.LogWarning(
                    exception,
                    "Status refresh for RasGate {RasGateId} was rejected",
                    rasGateId);
            }
    }
}