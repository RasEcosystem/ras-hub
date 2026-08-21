using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers.RasGates;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("RasGates")]
[ControllerDescription("RasGates",
    "Inspect and manage Hub-owned RasGate registrations and their observed status.")]
public sealed class RasGateQueryController(
    RasGateQueries queries) : ControllerBase
{
    [HttpGet(Name = "GetPagedRasGates")]
    [EndpointSummary("Get paged gateways")]
    [EndpointDescription(
        "Returns one page of Hub-owned RasGate registrations from the database. Stored API keys are never returned.")]
    [ProducesResponseType<ApiResponse<PageResult<RasGateModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<PageResult<RasGateModel>>> GetPaged(
        [FromQuery] PageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetPagedAsync(
            request,
            cancellationToken);

        return ApiResponse<PageResult<RasGateModel>>.Ok(result);
    }

    [HttpGet("all", Name = "GetAllRasGates")]
    [EndpointSummary("Get all gateways")]
    [EndpointDescription(
        "Returns all Hub-owned RasGate registrations from the database without pagination. Stored API keys are never returned.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RasGateModel>>>(
        StatusCodes.Status200OK)]
    public async Task<ApiResponse<IReadOnlyList<RasGateModel>>> GetAll(
        CancellationToken cancellationToken)
    {
        var result = await queries.GetAllAsync(cancellationToken);

        return ApiResponse<IReadOnlyList<RasGateModel>>.Ok(result);
    }

    [HttpGet("{rasGateId:guid}", Name = "GetRasGate")]
    [EndpointSummary("Get gateway")]
    [EndpointDescription(
        "Returns one Hub-owned RasGate registration from the database. The stored API key is never returned.")]
    [ProducesResponseType<ApiResponse<RasGateModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status404NotFound)]
    public async Task<ApiResponse<RasGateModel>> GetOne(
        Guid rasGateId,
        CancellationToken cancellationToken)
    {
        var rasGate = await queries.GetByIdAsync(
            rasGateId,
            cancellationToken);

        return rasGate is null
            ? RasGateApiResponses.GateNotFound<RasGateModel>(rasGateId)
            : ApiResponse<RasGateModel>.Ok(rasGate);
    }
}
