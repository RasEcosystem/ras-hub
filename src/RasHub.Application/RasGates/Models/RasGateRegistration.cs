namespace RasHub.Application.RasGates.Models;

public sealed record RasGateRegistration(
    string Name,
    string Url,
    int Port,
    string ApiKey,
    bool IsActive);

public sealed record RasGateRegistrationUpdate(
    string Name,
    string Url,
    int Port,
    bool IsActive,
    long ExpectedConfigurationRevision,
    string? ApiKey);
