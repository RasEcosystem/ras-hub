namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateApiKeyRequiredException()
    : Exception(
        "A new RasGate API key is required when the endpoint changes.");
