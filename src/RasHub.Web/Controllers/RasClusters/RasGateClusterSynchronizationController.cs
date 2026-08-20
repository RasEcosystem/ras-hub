using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks.Clusters;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Responses;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.RasGates;

namespace RasHub.Web.Controllers.RasClusters;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates/{rasGateId:guid}/clusters")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("Clusters")]
[ControllerDescription("Clusters",
    "Inspect, synchronize, and manage 1C:Enterprise clusters through a registered gateway.")]
public sealed class RasGateClusterSynchronizationController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost("{clusterId:guid}/synchronize", Name = "SynchronizeCluster")]
    [EndpointSummary("Synchronize cluster")]
    [EndpointDescription(
        "Synchronizes one cluster through RAC cluster info, persists it, and returns the synchronized cluster.")]
    [ProducesResponseType<ApiResponse<ClusterModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<ClusterModel>> SynchronizeCluster(
        Guid rasGateId,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<ClusterModel>(
                state,
                rasGateId);

        var execution = await taskRunner.RunAsync(
            new SynchronizeClusterTask(rasGateId, clusterId),
            RasGateTaskOptions.InteractiveClusterSynchronization(
                rasGateId,
                clusterId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.ClusterSynchronizationRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.ClusterSynchronizationFailed(
                taskResult);

        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        return cluster is null
            ? RasGateApiResponses.ClusterNotFound(clusterId)
            : ApiResponse<ClusterModel>.Ok(cluster);
    }

    [HttpPost("synchronize", Name = "SynchronizeClusters")]
    [EndpointSummary("Synchronize clusters")]
    [EndpointDescription(
        "Synchronizes the complete cluster collection, persists it, and returns synchronization metadata.")]
    [ProducesResponseType<ApiResponse<CollectionSynchronizationResponse>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<CollectionSynchronizationResponse>> SynchronizeClusters(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses
                .ForUnavailableGate<CollectionSynchronizationResponse>(
                    state,
                    rasGateId);

        var execution = await taskRunner.RunWithResultAsync<
            SynchronizeClustersTask,
            CollectionSynchronizationResult>(
            new SynchronizeClustersTask(rasGateId),
            RasGateTaskOptions.InteractiveClustersSynchronization(rasGateId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.ClustersSynchronizationRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.ClustersSynchronizationFailed(
                taskResult);

        var result = execution.Value!;

        return ApiResponse<CollectionSynchronizationResponse>.Ok(
            new CollectionSynchronizationResponse { TotalCount = result.TotalCount, ObservedAt = result.ObservedAt });
    }
}