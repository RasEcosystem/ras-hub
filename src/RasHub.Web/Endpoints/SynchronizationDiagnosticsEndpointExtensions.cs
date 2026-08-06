using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Models;

namespace RasHub.Web.Endpoints;

/// <summary>
///     Maps authenticated, read-only development endpoints for inspecting the Synchronization Engine.
/// </summary>
internal static class SynchronizationDiagnosticsEndpointExtensions
{
    private const int DefaultTaskLimit = 100;
    private const int MaximumTaskLimit = 1_000;

    public static IEndpointRouteBuilder MapSynchronizationDiagnostics(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/dev/synchronization")
            .WithTags("Development / Synchronization")
            .RequireAuthorization();

        group
            .MapGet("/", (ISynchronizationMonitor monitor) =>
                Results.Ok(monitor.GetSnapshot()))
            .WithName("GetSynchronizationSnapshot")
            .Produces<SynchronizationMonitorSnapshot>();

        group
            .MapGet("/tasks", GetTasks)
            .WithName("GetSynchronizationTasks")
            .Produces<IReadOnlyList<BackgroundTaskSnapshot>>()
            .ProducesValidationProblem();

        group
            .MapGet("/tasks/{taskId:guid}", GetTask)
            .WithName("GetSynchronizationTask")
            .Produces<BackgroundTaskSnapshot>()
            .Produces(StatusCodes.Status404NotFound);

        group
            .MapGet("/schedules", (ISynchronizationMonitor monitor) =>
                Results.Ok(monitor.GetSchedules()))
            .WithName("GetSynchronizationSchedules")
            .Produces<IReadOnlyList<BackgroundTaskScheduleSnapshot>>();

        return endpoints;
    }

    private static IResult GetTasks(
        BackgroundTaskState? state,
        BackgroundTaskQueue? queue,
        int? limit,
        ISynchronizationMonitor monitor)
    {
        var effectiveLimit = limit ?? DefaultTaskLimit;

        if (effectiveLimit is < 1 or > MaximumTaskLimit)
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["limit"] =
                    [
                        $"Limit must be between 1 and {MaximumTaskLimit}."
                    ]
                });

        return Results.Ok(
            monitor.GetTasks(state, queue, effectiveLimit));
    }

    private static IResult GetTask(
        Guid taskId,
        ISynchronizationMonitor monitor)
    {
        var task = monitor.GetTask(taskId);

        return task is null
            ? Results.NotFound()
            : Results.Ok(task);
    }
}