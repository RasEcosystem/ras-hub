using RasHub.Domain.Abstractions;

namespace RasHub.Domain;

public sealed class RasInfobase : IEntity, IAuditable, ISoftDeletable
{
    public Guid RasClusterId { get; set; }

    public Guid ExternalId { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public DateTime ObservedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid Id { get; } = Guid.NewGuid();

    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}