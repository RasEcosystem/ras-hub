using RasHub.Domain.Abstractions;

namespace RasHub.Domain;

public sealed class RasGate : IEntity, IAuditable, ISoftDeletable
{
    public const int NameMaxLength = 200;
    public const int UrlMaxLength = 2_048;
    public const int ApiKeyMaxLength = 512;

    public required string Name { get; set; }

    public required string Url { get; set; }

    public int Port { get; set; }

    public required string ApiKey { get; set; }

    public string? InstanceName { get; set; }

    public string? Version { get; set; }

    public DateTime? StatusObservedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid Id { get; } = Guid.NewGuid();

    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}