using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers.RasClusters;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates/{rasGateId:guid}/clusters")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("Clusters")]
[ControllerDescription("Clusters",
    "Manage 1C:Enterprise clusters, inspect their persisted shadow, and refresh it from live RasGate data.")]
public sealed class RasGateClusterShadowController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries) : ControllerBase
{
    [HttpGet("shadow", Name = "GetShadowPagedClusters")]
    [EndpointSummary("Get paged cluster shadow")]
    [EndpointDescription(
        "Returns one page from the persisted cluster shadow without contacting RasGate.")]
    [ProducesResponseType<ApiResponse<PageResult<ClusterModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<PageResult<ClusterModel>>> GetShadowPaged(
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

        var result = await clusterQueries.GetPagedAsync(
            rasGateId,
            request,
            cancellationToken);

        return ApiResponse<PageResult<ClusterModel>>.Ok(result);
    }

    [HttpGet("shadow/all", Name = "GetShadowAllClusters")]
    [EndpointSummary("Get all cluster shadow entries")]
    [EndpointDescription(
        "Returns the complete persisted cluster shadow without pagination and without contacting RasGate.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ClusterModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<IReadOnlyList<ClusterModel>>> GetShadowAll(
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

        var result = await clusterQueries.GetAllAsync(
            rasGateId,
            cancellationToken);

        return ApiResponse<IReadOnlyList<ClusterModel>>.Ok(result);
    }

    [HttpGet("shadow/{clusterId:guid}", Name = "GetShadowCluster")]
    [EndpointSummary("Get cluster shadow entry")]
    [EndpointDescription(
        "Returns one cluster from the persisted shadow without contacting RasGate.")]
    [ProducesResponseType<ApiResponse<ClusterModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<ClusterModel>> GetShadowOne(
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

        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        return cluster is null
            ? RasGateApiResponses.ClusterNotFound(clusterId)
            : ApiResponse<ClusterModel>.Ok(cluster);
    }
}
