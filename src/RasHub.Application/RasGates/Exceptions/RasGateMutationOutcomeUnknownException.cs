using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateMutationOutcomeUnknownException
    : NonRetryableBackgroundTaskException
{
    public RasGateMutationOutcomeUnknownException(
        Guid rasGateId,
        string resource,
        string operation)
        : base(CreateMessage(rasGateId, resource, operation))
    {
        RasGateId = rasGateId;
        Resource = resource;
        Operation = operation;
    }

    public Guid RasGateId { get; }

    public string Resource { get; }

    public string Operation { get; }

    private static string CreateMessage(
        Guid rasGateId,
        string resource,
        string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        return $"RasGate '{rasGateId}' could not confirm the outcome of " +
               $"'{resource}.{operation}'.";
    }
}
