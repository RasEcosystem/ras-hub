using RasHub.Application.RasGates.Models;

namespace RasHub.Infrastructure.RasGates.Rac.Adapters;

public interface IRacResourceAdapterDescriptor
{
    string Resource { get; }

    string Operation { get; }

    int SchemaVersion { get; }

    Version MinimumVersion { get; }

    int GetSchemaVersion(Version racVersion)
    {
        return SchemaVersion;
    }
}

public interface IRacResourceAdapter<T> : IRacResourceAdapterDescriptor
{
    IReadOnlyList<string> CreateCommand(Guid? externalId = null);

    RasResourceSnapshot<T> Parse(
        Version racVersion,
        RacExecutionResult execution,
        Guid? externalId = null);
}

public interface IRacCommandAdapter<in TCommand>
    : IRacResourceAdapterDescriptor
{
    IReadOnlyList<string> CreateCommand(TCommand command);

    void Validate(
        Version racVersion,
        RacExecutionResult execution,
        TCommand command);
}

public interface IRacResultCommandAdapter<in TCommand, out TResult>
    : IRacResourceAdapterDescriptor
{
    IReadOnlyList<string> CreateCommand(TCommand command);

    TResult Parse(
        Version racVersion,
        RacExecutionResult execution,
        TCommand command);
}
