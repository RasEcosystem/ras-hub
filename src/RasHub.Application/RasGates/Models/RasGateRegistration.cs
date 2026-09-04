namespace RasHub.Application.RasGates.Models;

public sealed record RasGateRegistration(
    string Name,
    string Url,
    int Port,
    string ApiKey,
    bool IsActive)
{
    public override string ToString()
    {
        return nameof(RasGateRegistration);
    }
}

public sealed record RasGateRegistrationUpdate(
    string Name,
    string Url,
    int Port,
    bool IsActive,
    long ExpectedConfigurationRevision,
    string? ApiKey)
{
    public override string ToString()
    {
        return nameof(RasGateRegistrationUpdate);
    }
}
