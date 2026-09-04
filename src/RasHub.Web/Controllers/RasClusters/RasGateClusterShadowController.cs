using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasEndpoints;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers.RasClusters;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-endpoints/{rasEndpointId:guid}/clusters")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("Clusters")]
[ControllerDescription("Clusters",
    "Manage 1C:Enterprise clusters owned by a RAS endpoint, inspect their persisted shadow, and refresh it through the assigned RasGate.")]
public sealed class RasGateClusterShadowController(
    ActiveRasEndpointLookup rasEndpointLookup,
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

        var result = await clusterQueries.GetPagedAsync(
            rasEndpointId,
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

        var result = await clusterQueries.GetAllAsync(
            rasEndpointId,
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

        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasEndpointId,
            clusterId,
            cancellationToken);

        return cluster is null
            ? RasGateApiResponses.ClusterNotFound(clusterId)
            : ApiResponse<ClusterModel>.Ok(cluster);
    }
}
