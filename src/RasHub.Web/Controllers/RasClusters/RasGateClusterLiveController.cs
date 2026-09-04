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
using RasHub.Web.Api.RasEndpoints;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.RasGates;

namespace RasHub.Web.Controllers.RasClusters;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-endpoints/{rasEndpointId:guid}/clusters")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("Clusters")]
[ControllerDescription("Clusters",
    "Manage 1C:Enterprise clusters owned by a RAS endpoint, inspect their persisted shadow, and refresh it through the assigned RasGate.")]
public sealed class RasGateClusterLiveController(
    ActiveRasEndpointLookup rasEndpointLookup,
    RasClusterQueries clusterQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost("live", Name = "GetLivePagedClusters")]
    [EndpointSummary("Get paged live clusters")]
    [EndpointDescription(
        "Fetches the complete live cluster snapshot from the RAS endpoint through its assigned RasGate, atomically refreshes the persisted shadow, and returns one page from the updated shadow. Pagination limits only the HTTP response.")]
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
        Guid rasEndpointId,
        [FromQuery] PageRequest request,
        CancellationToken cancellationToken)
    {
        var state = await rasEndpointLookup.GetStateAsync(
            rasEndpointId,
            cancellationToken);

        if (state != ActiveRasEndpointState.Active)
            return RasEndpointApiResponses
                .ForUnavailableEndpoint<PageResult<ClusterModel>>(
                    state,
                    rasEndpointId);

        var execution = await RefreshShadowAsync(
            rasEndpointId,
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
            rasEndpointId,
            request,
            cancellationToken);

        return ApiResponse<PageResult<ClusterModel>>.Ok(clusters);
    }

    [HttpPost("live/all", Name = "GetLiveAllClusters")]
    [EndpointSummary("Get all live clusters")]
    [EndpointDescription(
        "Fetches the complete live cluster snapshot from the RAS endpoint through its assigned RasGate, atomically refreshes the persisted shadow, and returns the complete updated shadow without pagination.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ClusterModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<IReadOnlyList<ClusterModel>>> GetLiveAll(
        Guid rasEndpointId,
        CancellationToken cancellationToken)
    {
        var state = await rasEndpointLookup.GetStateAsync(
            rasEndpointId,
            cancellationToken);

        if (state != ActiveRasEndpointState.Active)
            return RasEndpointApiResponses
                .ForUnavailableEndpoint<IReadOnlyList<ClusterModel>>(
                    state,
                    rasEndpointId);

        var execution = await RefreshShadowAsync(
            rasEndpointId,
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
            rasEndpointId,
            cancellationToken);

        return ApiResponse<IReadOnlyList<ClusterModel>>.Ok(clusters);
    }

    [HttpPost("live/{clusterId:guid}", Name = "GetLiveCluster")]
    [EndpointSummary("Get live cluster")]
    [EndpointDescription(
        "Fetches one live cluster from the RAS endpoint through its assigned RasGate, refreshes that entry in the persisted shadow without changing siblings, and returns the updated shadow entry.")]
    [ProducesResponseType<ApiResponse<ClusterModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<ClusterModel>> GetLiveOne(
        Guid rasEndpointId,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        var state = await rasEndpointLookup.GetStateAsync(
            rasEndpointId,
            cancellationToken);

        if (state != ActiveRasEndpointState.Active)
            return RasEndpointApiResponses.ForUnavailableEndpoint<ClusterModel>(
                state,
                rasEndpointId);

        var execution = await taskRunner.RunAsync(
            new SynchronizeClusterTask(rasEndpointId, clusterId),
            RasGateTaskOptions.InteractiveClusterSynchronization(
                rasEndpointId,
                clusterId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.ClusterLiveRefreshRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.ClusterLiveRefreshFailed(
                taskResult);

        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasEndpointId,
            clusterId,
            cancellationToken);

        return cluster is null
            ? RasGateApiResponses.ClusterNotFound(clusterId)
            : ApiResponse<ClusterModel>.Ok(cluster);
    }

    [HttpPost("shadow/refresh", Name = "RefreshClusterShadow")]
    [EndpointSummary("Refresh cluster shadow")]
    [EndpointDescription(
        "Fetches the complete live cluster snapshot from the RAS endpoint through its assigned RasGate, atomically refreshes the persisted shadow, and returns refresh metadata without returning the collection.")]
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
            Guid rasEndpointId,
            CancellationToken cancellationToken)
    {
        var state = await rasEndpointLookup.GetStateAsync(
            rasEndpointId,
            cancellationToken);

        if (state != ActiveRasEndpointState.Active)
            return RasEndpointApiResponses
                .ForUnavailableEndpoint<ShadowRefreshResponse>(
                    state,
                    rasEndpointId);

        var execution = await RefreshShadowAsync(
            rasEndpointId,
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
            new ShadowRefreshResponse { TotalCount = result.TotalCount, ObservedAt = result.ObservedAt });
    }

    private Task<InteractiveTaskExecution<CollectionSynchronizationResult>>
        RefreshShadowAsync(
            Guid rasEndpointId,
            CancellationToken cancellationToken)
    {
        return taskRunner.RunWithResultAsync<
            SynchronizeClustersTask,
            CollectionSynchronizationResult>(
            new SynchronizeClustersTask(rasEndpointId),
            RasGateTaskOptions.InteractiveClustersSynchronization(
                rasEndpointId),
            cancellationToken);
    }
}
