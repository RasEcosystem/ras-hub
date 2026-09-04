namespace RasHub.Application.RasEndpoints.Exceptions;

public sealed class RasEndpointRevisionConflictException(Guid rasEndpointId)
    : InvalidOperationException(
        $"RAS endpoint '{rasEndpointId}' changed concurrently.")
{
    public Guid RasEndpointId { get; } = rasEndpointId;
}
