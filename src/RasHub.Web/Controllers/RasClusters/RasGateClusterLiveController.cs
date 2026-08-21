using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks.Clusters;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
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
    "Manage 1C:Enterprise clusters, inspect their persisted shadow, and refresh it from live RasGate data.")]
public sealed class RasGateClusterLiveController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost("live", Name = "GetLivePagedClusters")]
    [EndpointSummary("Get paged live clusters")]
    [EndpointDescription(
        "Fetches the complete live cluster snapshot from RasGate, atomically refreshes the persisted shadow, and returns one page from the updated shadow. Pagination limits only the HTTP response; RasGate is queried for the complete snapshot.")]
    [ProducesResponseType<ApiResponse<PageResult<ClusterModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<PageResult<ClusterModel>>> GetLivePaged(
        Guid rasGateId,
        [FromQuery] PageRequest request,
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

        var execution = await RefreshShadowAsync(
            rasGateId,
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses
                .ClusterLiveRefreshRejected<PageResult<ClusterModel>>();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses
                .ClusterLiveRefreshFailed<PageResult<ClusterModel>>(
                    taskResult);

        var clusters = await clusterQueries.GetPagedAsync(
            rasGateId,
            request,
            cancellationToken);

        return ApiResponse<PageResult<ClusterModel>>.Ok(clusters);
    }

    [HttpPost("live/all", Name = "GetLiveAllClusters")]
    [EndpointSummary("Get all live clusters")]
    [EndpointDescription(
        "Fetches the complete live cluster snapshot from RasGate, atomically refreshes the persisted shadow, and returns the complete updated shadow without pagination.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ClusterModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<IReadOnlyList<ClusterModel>>> GetLiveAll(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses
                .ForUnavailableGate<IReadOnlyList<ClusterModel>>(
                    state,
                    rasGateId);

        var execution = await RefreshShadowAsync(
            rasGateId,
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses
                .ClusterLiveRefreshRejected<IReadOnlyList<ClusterModel>>();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses
                .ClusterLiveRefreshFailed<IReadOnlyList<ClusterModel>>(
                    taskResult);

        var clusters = await clusterQueries.GetAllAsync(
            rasGateId,
            cancellationToken);

        return ApiResponse<IReadOnlyList<ClusterModel>>.Ok(clusters);
    }

    [HttpPost("live/{clusterId:guid}", Name = "GetLiveCluster")]
    [EndpointSummary("Get live cluster")]
    [EndpointDescription(
        "Fetches one live cluster from RasGate, refreshes that entry in the persisted shadow without changing siblings, and returns the updated shadow entry.")]
    [ProducesResponseType<ApiResponse<ClusterModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<ClusterModel>> GetLiveOne(
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
            return RasGateApiResponses.ClusterLiveRefreshRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.ClusterLiveRefreshFailed(
                taskResult);

        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        return cluster is null
            ? RasGateApiResponses.ClusterNotFound(clusterId)
            : ApiResponse<ClusterModel>.Ok(cluster);
    }

    [HttpPost("shadow/refresh", Name = "RefreshClusterShadow")]
    [EndpointSummary("Refresh cluster shadow")]
    [EndpointDescription(
        "Fetches the complete live cluster snapshot from RasGate, atomically refreshes the persisted shadow, and returns refresh metadata without returning the collection.")]
    [ProducesResponseType<ApiResponse<ShadowRefreshResponse>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<ShadowRefreshResponse>>
        RefreshShadow(
            Guid rasGateId,
            CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses
                .ForUnavailableGate<ShadowRefreshResponse>(
                    state,
                    rasGateId);

        var execution = await RefreshShadowAsync(
            rasGateId,
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses
                .ClusterShadowRefreshRejected<ShadowRefreshResponse>();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses
                .ClusterShadowRefreshFailed<ShadowRefreshResponse>(
                    taskResult);

        var result = execution.Value!;

        return ApiResponse<ShadowRefreshResponse>.Ok(
            new ShadowRefreshResponse
            {
                TotalCount = result.TotalCount,
                ObservedAt = result.ObservedAt
            });
    }

    private Task<InteractiveTaskExecution<CollectionSynchronizationResult>>
        RefreshShadowAsync(
            Guid rasGateId,
            CancellationToken cancellationToken)
    {
        return taskRunner.RunWithResultAsync<
            SynchronizeClustersTask,
            CollectionSynchronizationResult>(
            new SynchronizeClustersTask(rasGateId),
            RasGateTaskOptions.InteractiveClustersSynchronization(rasGateId),
            cancellationToken);
    }
}
