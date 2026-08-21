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
[Tags("Infobases")]
[ControllerDescription("Infobases",
    "Inspect the persisted infobase shadow and refresh it from live RasGate data.")]
public sealed class RasClusterInfobaseShadowController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    RasInfobaseQueries infobaseQueries) : ControllerBase
{
    [HttpGet("shadow", Name = "GetShadowPagedInfobases")]
    [EndpointSummary("Get paged infobase shadow")]
    [EndpointDescription(
        "Returns one page from the persisted infobase shadow without contacting RasGate.")]
    [ProducesResponseType<ApiResponse<PageResult<InfobaseModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<PageResult<InfobaseModel>>> GetShadowPaged(
        Guid rasGateId,
        Guid clusterId,
        [FromQuery] PageRequest request,
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

    [HttpGet("shadow/all", Name = "GetShadowAllInfobases")]
    [EndpointSummary("Get all infobase shadow entries")]
    [EndpointDescription(
        "Returns the complete persisted infobase shadow without pagination and without contacting RasGate.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<InfobaseModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<IReadOnlyList<InfobaseModel>>> GetShadowAll(
        Guid rasGateId,
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses
                .ForUnavailableGate<IReadOnlyList<InfobaseModel>>(
                    state,
                    rasGateId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasGateId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses
                .ClusterNotFound<IReadOnlyList<InfobaseModel>>(clusterId);

        var result = await infobaseQueries.GetAllAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        return ApiResponse<IReadOnlyList<InfobaseModel>>.Ok(result);
    }

    [HttpGet("shadow/{infobaseId:guid}", Name = "GetShadowInfobase")]
    [EndpointSummary("Get infobase shadow entry")]
    [EndpointDescription(
        "Returns one infobase from the persisted shadow without contacting RasGate.")]
    [ProducesResponseType<ApiResponse<InfobaseModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<InfobaseModel>> GetShadowOne(
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
