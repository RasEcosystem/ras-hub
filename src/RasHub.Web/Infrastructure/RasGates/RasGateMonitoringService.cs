using Microsoft.Extensions.Options;
using RasHub.Application.RasGates.Tasks.Status;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.Infrastructure.Database.Queries;

namespace RasHub.Web.Infrastructure.RasGates;

public sealed class RasGateMonitoringService(
    IServiceScopeFactory scopeFactory,
    IBackgroundTaskEngine backgroundTaskEngine,
    IOptions<RasGateMonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<RasGateMonitoringService> logger)
    : BackgroundService
{
    private RasGateMonitoringOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Options.RunOnStartup)
            await TryEnqueueStatusChecksAsync(stoppingToken);

        using var timer = new PeriodicTimer(Options.PollInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TryEnqueueStatusChecksAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task TryEnqueueStatusChecksAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await EnqueueStatusChecksAsync(cancellationToken);
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

    private async Task EnqueueStatusChecksAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<RasGateQueries>();
        var rasGateIds = await queries.GetActiveIdsAsync(cancellationToken);

        foreach (var rasGateId in rasGateIds)
            try
            {
                backgroundTaskEngine.Enqueue(
                    new CheckRasGateStatusTask(rasGateId),
                    RasGateTaskOptions.StatusMonitoring(
                        rasGateId,
                        Options.RequestTimeout));
            }
            catch (BackgroundTaskRejectedException exception)
            {
                logger.LogWarning(
                    exception,
                    "Status check for RasGate {RasGateId} was rejected",
                    rasGateId);
            }
    }
}
