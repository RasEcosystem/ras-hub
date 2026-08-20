using RasHub.BackgroundTasks.Exceptions;

namespace RasHub.Application.RasGates.Exceptions;

public sealed class RacResourceNotFoundException(
    string resource,
    Guid externalId)
    : NonRetryableBackgroundTaskException(
        $"RAC resource '{resource}/{externalId}' was not found.")
{
    public string Resource { get; } = resource;

    public Guid ExternalId { get; } = externalId;
}
