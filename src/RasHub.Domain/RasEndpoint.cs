using RasHub.Domain.Abstractions;

namespace RasHub.Domain;

public sealed class RasEndpoint : IEntity, IAuditable, ISoftDeletable
{
    public const int NameMaxLength = 200;
    public const int HostMaxLength = 255;

    public required string Name { get; set; }

    public Guid RasGateId { get; set; }

    public required string Host { get; set; }

    public int Port { get; set; }

    public long ConfigurationRevision { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid Id { get; } = Guid.NewGuid();

    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
