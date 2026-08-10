namespace RasHub.BackgroundTasks.Exceptions;

/// <summary>
///     Indicates that the engine could not accept a task because it is stopping
///     or an in-memory admission limit was reached.
/// </summary>
public sealed class BackgroundTaskRejectedException
    : InvalidOperationException
{
    public BackgroundTaskRejectedException(Type taskType)
        : this(taskType, "the target queue is full")
    {
    }

    public BackgroundTaskRejectedException(
        Type taskType,
        string reason)
        : base($"Background task '{taskType.FullName}' was rejected because {reason}.")
    {
        TaskType = taskType;
    }

    public Type TaskType { get; }
}
