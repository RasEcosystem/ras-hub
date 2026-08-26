using RasHub.Domain.Abstractions;

namespace RasHub.Domain;

public sealed class RasGate : IEntity, IAuditable, ISoftDeletable
{
    public const int NameMaxLength = 200;
    public const int UrlMaxLength = 2_048;

    public required string Name { get; set; }

    public required string Url { get; set; }

    public int Port { get; set; }

    public required string ApiKey { get; set; }

    public long ConfigurationRevision { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public string? InstanceName { get; set; }

    public string? Version { get; set; }

    public DateTime? StatusObservedAt { get; set; }

    public bool? RacAvailable { get; set; }

    public string? RacVersion { get; set; }

    public DateTime? RacStatusObservedAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid Id { get; } = Guid.NewGuid();

    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
