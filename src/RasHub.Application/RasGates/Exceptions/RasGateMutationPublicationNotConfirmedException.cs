using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateMutationPublicationNotConfirmedException
    : NonRetryableBackgroundTaskException
{
    public RasGateMutationPublicationNotConfirmedException(
        Guid rasGateId,
        string resource,
        string operation,
        Guid externalId)
        : base(CreateMessage(rasGateId, resource, operation, externalId))
    {
        RasGateId = rasGateId;
        Resource = resource;
        Operation = operation;
        ExternalId = externalId;
    }

    public RasGateMutationPublicationNotConfirmedException(
        Guid rasGateId,
        string resource,
        string operation,
        Guid externalId,
        Exception innerException)
        : base(
            CreateMessage(rasGateId, resource, operation, externalId),
            innerException)
    {
        RasGateId = rasGateId;
        Resource = resource;
        Operation = operation;
        ExternalId = externalId;
    }

    public Guid RasGateId { get; }

    public string Resource { get; }

    public string Operation { get; }

    public Guid ExternalId { get; }

    private static string CreateMessage(
        Guid rasGateId,
        string resource,
        string operation,
        Guid externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        return $"RasGate '{rasGateId}' confirmed '{resource}.{operation}', " +
               $"but publication of '{resource}/{externalId}' could not be confirmed.";
    }
}
