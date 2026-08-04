namespace RasHub.Domain.Abstractions;

public interface IAuditable
{
    DateTime UpdatedAt { get; set; }

    DateTime CreatedAt { get; set; }
}