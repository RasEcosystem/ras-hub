using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Commands;

namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasClusterGateway(
    RasGateSessionFactory sessionFactory,
    RacResourceAdapterResolver<RasClusterSnapshot> readAdapterResolver,
    RacResultCommandAdapterResolver<RasClusterCreationOptions, Guid>
        insertAdapterResolver,
    RacCommandAdapterResolver<UpdateRasClusterCommand> updateAdapterResolver,
    RacCommandAdapterResolver<RemoveRasClusterCommand> removeAdapterResolver)
    : IRasClusterGateway
{
    public Task<RasGateCapabilities> GetCapabilitiesAsync(
        RasGate rasGate,
        CancellationToken cancellationToken)
    {
        return sessionFactory
            .Create(rasGate)
            .GetCapabilitiesAsync(cancellationToken);
    }

    public async Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
        RasGate rasGate,
        CancellationToken cancellationToken)
    {
        var session = sessionFactory.Create(rasGate);
        var racVersion = await session.GetRacVersionAsync(cancellationToken);
        var adapter = readAdapterResolver.Resolve(
            "clusters",
            "snapshot",
            racVersion);
        var execution = await session.ExecuteRacQueryAsync(
            adapter.CreateCommand(),
            cancellationToken);

        return session.ParseRacOutput(() =>
            adapter.Parse(racVersion, execution));
    }

    public async Task<RasClusterSnapshot> GetClusterAsync(
        RasGate rasGate,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        var session = sessionFactory.Create(rasGate);
        var racVersion = await session.GetRacVersionAsync(cancellationToken);
        var adapter = readAdapterResolver.Resolve(
            "clusters",
            "info",
            racVersion);
        var execution = await session.ExecuteRacQueryAsync(
            adapter.CreateCommand(clusterId),
            cancellationToken);
        var snapshot = session.ParseRacOutput(() => adapter.Parse(
            racVersion,
            execution,
            clusterId));

        if (snapshot.Completeness != SnapshotCompleteness.Complete ||
            snapshot.Items.Count != 1)
            throw new RasGateClientException(
                "RasGate returned an incomplete cluster result.");

        return snapshot.Items[0];
    }

    public async Task<Guid> CreateClusterAsync(
        RasGate rasGate,
        RasClusterCreationOptions options,
        CancellationToken cancellationToken)
    {
        var session = sessionFactory.Create(rasGate);
        var racVersion = await session.GetRacVersionAsync(cancellationToken);
        var adapter = insertAdapterResolver.Resolve(
            "clusters",
            "insert",
            racVersion);
        var execution = await session.ExecuteRacMutationAsync(
            adapter.CreateCommand(options),
            "clusters",
            "insert",
            cancellationToken);

        return session.ParseRacMutationOutput(
            () => adapter.Parse(racVersion, execution, options),
            "clusters",
            "insert");
    }

    public async Task UpdateClusterAsync(
        RasGate rasGate,
        Guid clusterId,
        RasClusterUpdateOptions options,
        CancellationToken cancellationToken)
    {
        var session = sessionFactory.Create(rasGate);
        var racVersion = await session.GetRacVersionAsync(cancellationToken);
        var adapter = updateAdapterResolver.Resolve(
            "clusters",
            "update",
            racVersion);
        var command = new UpdateRasClusterCommand(clusterId, options);
        var execution = await session.ExecuteRacMutationAsync(
            adapter.CreateCommand(command),
            "clusters",
            "update",
            cancellationToken);

        adapter.Validate(racVersion, execution, command);
    }

    public async Task RemoveClusterAsync(
        RasGate rasGate,
        Guid clusterId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken)
    {
        var session = sessionFactory.Create(rasGate);
        var racVersion = await session.GetRacVersionAsync(cancellationToken);
        var adapter = removeAdapterResolver.Resolve(
            "clusters",
            "remove",
            racVersion);
        var command = new RemoveRasClusterCommand(
            clusterId,
            clusterUser,
            clusterPassword);
        var execution = await session.ExecuteRacMutationAsync(
            adapter.CreateCommand(command),
            "clusters",
            "remove",
            cancellationToken);

        adapter.Validate(racVersion, execution, command);
    }
}
