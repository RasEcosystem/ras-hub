namespace RasHub.Application.RasEndpoints.Models;

public sealed record RasEndpointRegistration(
    string Name,
    string Host,
    int Port,
    bool IsActive);

public sealed record RasEndpointRegistrationUpdate(
    string Name,
    string Host,
    int Port,
    bool IsActive,
    long ExpectedConfigurationRevision);
