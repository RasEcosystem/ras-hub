namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateEndpointValidationException(string message)
    : Exception(message);
