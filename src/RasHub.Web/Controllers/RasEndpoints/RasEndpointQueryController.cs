using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasEndpoints;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers.RasEndpoints;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-endpoints")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("RasEndpoints")]
[ControllerDescription("RasEndpoints",
    "Inspect and manage Hub-owned RAS administration endpoints.")]
public sealed class RasEndpointQueryController(
    RasEndpointQueries queries) : ControllerBase
{
    [HttpGet(Name = "GetPagedRasEndpoints")]
    [EndpointSummary("Get paged RAS endpoints")]
    [EndpointDescription(
        "Returns one page of Hub-owned RAS administration endpoints from the database.")]
    [ProducesResponseType<ApiResponse<PageResult<RasEndpointModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<PageResult<RasEndpointModel>>> GetPaged(
        [FromQuery] PageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetPagedAsync(request, cancellationToken);
        return ApiResponse<PageResult<RasEndpointModel>>.Ok(result);
    }

    [HttpGet("all", Name = "GetAllRasEndpoints")]
    [EndpointSummary("Get all RAS endpoints")]
    [EndpointDescription(
        "Returns all Hub-owned RAS administration endpoints without pagination.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RasEndpointModel>>>(
        StatusCodes.Status200OK)]
    public async Task<ApiResponse<IReadOnlyList<RasEndpointModel>>> GetAll(
        CancellationToken cancellationToken)
    {
        var result = await queries.GetAllAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<RasEndpointModel>>.Ok(result);
    }

    [HttpGet("{rasEndpointId:guid}", Name = "GetRasEndpoint")]
    [EndpointSummary("Get RAS endpoint")]
    [EndpointDescription(
        "Returns one Hub-owned RAS administration endpoint from the database.")]
    [ProducesResponseType<ApiResponse<RasEndpointModel>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status404NotFound)]
    public async Task<ApiResponse<RasEndpointModel>> GetOne(
        Guid rasEndpointId,
        CancellationToken cancellationToken)
    {
        var rasEndpoint = await queries.GetByIdAsync(
            rasEndpointId,
            cancellationToken);

        return rasEndpoint is null
            ? RasEndpointApiResponses.EndpointNotFound<RasEndpointModel>(
                rasEndpointId)
            : ApiResponse<RasEndpointModel>.Ok(rasEndpoint);
    }
}
