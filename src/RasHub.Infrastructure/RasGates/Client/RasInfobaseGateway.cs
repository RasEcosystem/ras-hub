using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Commands;

namespace RasHub.Infrastructure.RasGates.Client;

internal sealed class RasInfobaseGateway(
    RasGateSessionFactory sessionFactory,
    RacResultCommandAdapterResolver<
        RacInfobaseQuery,
        RasResourceSnapshot<RasInfobaseSnapshot>> adapterResolver)
    : IRasInfobaseGateway
{
    public Task<RasGateCapabilities> GetCapabilitiesAsync(
        RasGate rasGate,
        CancellationToken cancellationToken)
    {
        return sessionFactory
            .Create(rasGate)
            .GetCapabilitiesAsync(cancellationToken);
    }

    public async Task<RasResourceSnapshot<RasInfobaseSnapshot>>
        GetInfobasesAsync(
            RasGate rasGate,
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
    {
        var session = sessionFactory.Create(rasGate);
        var racVersion = await session.GetRacVersionAsync(cancellationToken);
        var adapter = adapterResolver.Resolve(
            "infobases",
            "snapshot",
            racVersion);
        var command = new RacInfobaseQuery(
            clusterId,
            clusterUser: clusterUser,
            clusterPassword: clusterPassword);
        var execution = await session.ExecuteRacQueryAsync(
            adapter.CreateCommand(command),
            cancellationToken);

        return session.ParseRacOutput(() =>
            adapter.Parse(racVersion, execution, command));
    }

    public async Task<RasInfobaseSnapshot> GetInfobaseAsync(
        RasGate rasGate,
        Guid clusterId,
        Guid infobaseId,
        string? clusterUser,
        string? clusterPassword,
        CancellationToken cancellationToken)
    {
        var session = sessionFactory.Create(rasGate);
        var racVersion = await session.GetRacVersionAsync(cancellationToken);
        var adapter = adapterResolver.Resolve(
            "infobases",
            "info",
            racVersion);
        var command = new RacInfobaseQuery(
            clusterId,
            infobaseId,
            clusterUser,
            clusterPassword);
        var execution = await session.ExecuteRacQueryAsync(
            adapter.CreateCommand(command),
            cancellationToken);
        var snapshot = session.ParseRacOutput(() =>
            adapter.Parse(racVersion, execution, command));

        if (snapshot.Completeness != SnapshotCompleteness.Complete ||
            snapshot.Items.Count != 1)
            throw new RasGateClientException(
                "RasGate returned an incomplete infobase result.");

        return snapshot.Items[0];
    }
}