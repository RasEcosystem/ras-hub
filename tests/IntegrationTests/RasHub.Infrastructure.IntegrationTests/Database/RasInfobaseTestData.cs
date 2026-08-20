using RasHub.Domain;

namespace RasHub.Infrastructure.IntegrationTests.Database;

internal static class RasInfobaseTestData
{
    public static RasInfobase Create(
        Guid rasClusterId,
        Guid? externalId = null,
        string name = "Main infobase",
        string description = "Primary database")
    {
        return new RasInfobase
        {
            RasClusterId = rasClusterId,
            ExternalId = externalId ?? Guid.NewGuid(),
            Name = name,
            Description = description,
            ObservedAt = new DateTime(
                2026,
                8,
                20,
                0,
                0,
                0,
                DateTimeKind.Utc)
        };
    }
}