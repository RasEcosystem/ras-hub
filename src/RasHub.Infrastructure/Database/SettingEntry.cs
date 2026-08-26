namespace RasHub.Infrastructure.Database;

public sealed class SettingEntry
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
