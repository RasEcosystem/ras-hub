namespace RasHub.Domain.Abstractions;

public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }

    bool IsDeleted { get; set; }
}