namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasGateSessionState(
    Uri baseAddress,
    string apiKey,
    Guid rasGateId,
    long configurationRevision)
{
    public Uri BaseAddress { get; } = baseAddress;

    public string ApiKey { get; } = apiKey;

    public Guid RasGateId { get; } = rasGateId;

    public long ConfigurationRevision { get; } = configurationRevision;

    public override string ToString()
    {
        return $"{nameof(RasGateSessionState)} {{ " +
               $"BaseAddress = {BaseAddress}, " +
               "ApiKey = [REDACTED], " +
               $"RasGateId = {RasGateId}, " +
               $"ConfigurationRevision = {ConfigurationRevision} }}";
    }
}
