namespace RasHub.Infrastructure;

public sealed class RasHubOptions
{
    public const string SectionName = "RasHub";

    public string ApiKey { get; init; } = "";
}