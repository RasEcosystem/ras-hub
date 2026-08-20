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
[ControllerDescription(
    "Inspect cached 1C:Enterprise clusters available through a registered gateway.")]
public sealed class RasGateClustersController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries) : ControllerBase
{
    [HttpPost("get-paged")]
    [EndpointSummary("List clusters")]
    [EndpointDescription(
        "Returns cached clusters without contacting the gateway.")]
    [ProducesResponseType(
        typeof(ApiResponse<PageResult<ClusterModel>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<PageResult<ClusterModel>>> GetPaged(
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

        var result = await clusterQueries.GetPagedAsync(
            rasGateId,
            request,
            cancellationToken);

        return ApiResponse<PageResult<ClusterModel>>.Ok(result);
    }

    [HttpGet("{clusterId:guid}")]
    [EndpointSummary("Get cluster")]
    [EndpointDescription(
        "Returns a cached cluster without contacting the gateway.")]
    [ProducesResponseType(
        typeof(ApiResponse<ClusterModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<ClusterModel>> GetById(
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