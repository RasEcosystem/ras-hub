namespace RasHub.Application.RasGates.Models;

public sealed record CollectionSynchronizationResult(
    int TotalCount,
    DateTime ObservedAt);