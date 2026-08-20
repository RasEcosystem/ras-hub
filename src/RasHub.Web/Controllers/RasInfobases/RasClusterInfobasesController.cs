using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers.RasInfobases;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route(
    "api/v1/ras-gates/{rasGateId:guid}/clusters/{clusterId:guid}/infobases")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[ControllerDescription(
    "Inspect cached 1C:Enterprise infobases registered in a cluster.")]
public sealed class RasClusterInfobasesController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    RasInfobaseQueries infobaseQueries) : ControllerBase
{
    [HttpPost("get-paged")]
    [EndpointSummary("List infobases")]
    [EndpointDescription(
        "Returns cached infobases without contacting the gateway.")]
    [ProducesResponseType(
        typeof(ApiResponse<PageResult<InfobaseModel>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<PageResult<InfobaseModel>>> GetPaged(
        Guid rasGateId,
        Guid clusterId,
        [FromBody] PageRequest request,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses
                .ForUnavailableGate<PageResult<InfobaseModel>>(
                    state,
                    rasGateId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasGateId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses
                .ClusterNotFound<PageResult<InfobaseModel>>(clusterId);

        var result = await infobaseQueries.GetPagedAsync(
            rasGateId,
            clusterId,
            request,
            cancellationToken);

        return ApiResponse<PageResult<InfobaseModel>>.Ok(result);
    }

    [HttpGet("{infobaseId:guid}")]
    [EndpointSummary("Get infobase")]
    [EndpointDescription(
        "Returns a cached infobase without contacting the gateway.")]
    [ProducesResponseType(
        typeof(ApiResponse<InfobaseModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<InfobaseModel>> GetById(
        Guid rasGateId,
        Guid clusterId,
        Guid infobaseId,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<InfobaseModel>(
                state,
                rasGateId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasGateId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses.ClusterNotFound<InfobaseModel>(
                clusterId);

        var infobase = await infobaseQueries.GetByExternalIdAsync(
            rasGateId,
            clusterId,
            infobaseId,
            cancellationToken);

        return infobase is null
            ? RasGateApiResponses.InfobaseNotFound(infobaseId)
            : ApiResponse<InfobaseModel>.Ok(infobase);
    }
}