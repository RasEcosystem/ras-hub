namespace RasHub.Application.RasGates.Models;

public sealed record RasInfobaseSnapshot
{
    public required Guid ExternalId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }
}