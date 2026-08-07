namespace RasHub.Synchronization.Exceptions;

/// <summary>
///     Indicates that the Engine could not accept a task because a configured in-memory limit was reached.
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