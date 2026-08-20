using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.RasGates.Tasks.Clusters;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
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
[ControllerDescription(
    "Synchronize 1C:Enterprise clusters through a registered gateway.")]
public sealed class RasGateClusterSynchronizationController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost("{clusterId:guid}/synchronize")]
    [EndpointSummary("Synchronize cluster")]
    [EndpointDescription(
        "Synchronizes one cluster through RAC cluster info, persists it, and returns the synchronized cluster.")]
    [ProducesResponseType(
        typeof(ApiResponse<ClusterModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<ClusterModel>> SynchronizeById(
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

    [HttpPost("synchronize")]
    [EndpointSummary("Synchronize clusters")]
    [EndpointDescription(
        "Synchronizes clusters with the gateway, persists the snapshot, and returns the requested synchronized page.")]
    [ProducesResponseType(
        typeof(ApiResponse<PageResult<ClusterModel>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<PageResult<ClusterModel>>> Synchronize(
        Guid rasGateId,
        [FromBody] PageRequest request,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses
                .ForUnavailableGate<PageResult<ClusterModel>>(
                    state,
                    rasGateId);

        var execution = await taskRunner.RunAsync(
            new SynchronizeClustersTask(rasGateId),
            RasGateTaskOptions.InteractiveClustersSynchronization(rasGateId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.ClustersSynchronizationRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.ClustersSynchronizationFailed(
                taskResult);

        var result = await clusterQueries.GetPagedAsync(
            rasGateId,
            request,
            cancellationToken);

        return ApiResponse<PageResult<ClusterModel>>.Ok(result);
    }
}