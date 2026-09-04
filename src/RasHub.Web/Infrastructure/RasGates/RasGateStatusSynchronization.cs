using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Tasks.Status;
using RasHub.BackgroundTasks.Models;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.RasGates;

namespace RasHub.Web.Infrastructure.RasGates;

public sealed class RasGateStatusSynchronization(
    RasGateQueries queries,
    InteractiveTaskRunner taskRunner)
{
    public async Task<RasGateAdministrationResult> SynchronizeAsync(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var activity = await queries.GetActivityAsync(
            rasGateId,
            cancellationToken);
        if (activity is null)
            return RasGateAdministrationResult.Failure(
                "The RasGate no longer exists.");
        if (!activity.IsActive)
            return RasGateAdministrationResult.Failure(
                "Activate the RasGate before refreshing its status.");

        var execution = await taskRunner.RunAsync(
            new CheckRasGateStatusTask(rasGateId),
            RasGateTaskOptions.InteractiveStatusSynchronization(rasGateId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateAdministrationResult.Failure(
                "The status refresh could not be scheduled. Try again shortly.");

        var result = execution.Result!;
        if (result.IsSucceeded)
            return RasGateAdministrationResult.Success();

        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return RasGateAdministrationResult.Failure(
                "The status refresh was canceled.");

        if (result.Exception is RasGateNotFoundException)
            return RasGateAdministrationResult.Failure(
                "The RasGate no longer exists.");

        if (result.Exception is RasGateConfigurationChangedException)
            return RasGateAdministrationResult.Failure(
                "The RasGate configuration changed during the status refresh.");

        if (result.Exception is RasGateInactiveException)
            return RasGateAdministrationResult.Failure(
                "The RasGate was deactivated during the status refresh.");

        return RasGateAdministrationResult.Failure(
            "The RasGate did not return a valid status. Check its endpoint and credentials.");
    }
}
