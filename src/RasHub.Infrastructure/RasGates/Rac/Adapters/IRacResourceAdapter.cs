using RasHub.Application.RasGates.Models;

namespace RasHub.Infrastructure.RasGates.Rac.Adapters;

public interface IRacResourceAdapterDescriptor
{
    string Resource { get; }

    string Operation { get; }

    int SchemaVersion { get; }

    bool Supports(Version racVersion);
}

public interface IRacResourceAdapter<T> : IRacResourceAdapterDescriptor
{
    IReadOnlyList<string> CreateCommand(Guid? externalId = null);

    RasResourceSnapshot<T> Parse(
        Version racVersion,
        RacExecutionResult execution,
        Guid? externalId = null);
}