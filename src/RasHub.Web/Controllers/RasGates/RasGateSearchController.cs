using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests.Search;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers.RasGates;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates/search")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("RasGates")]
[ControllerDescription("RasGates",
    "Inspect and manage Hub-owned RasGate registrations and their observed status.")]
public sealed class RasGateSearchController(
    RasGateQueries queries) : ControllerBase
{
    [HttpGet(Name = "SearchPagedRasGates")]
    [EndpointSummary("Search paged gateways")]
    [EndpointDescription(
        "Searches Hub-owned RasGate registrations by a case-insensitive literal substring and returns one page. Supported fields are Name and Url; Name is searched when fields are omitted, and multiple fields are combined with OR. Stored API keys are never returned.")]
    [ProducesResponseType<ApiResponse<PageResult<RasGateModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<PageResult<RasGateModel>>> SearchPaged(
        [FromQuery] SearchRasGatesRequest search,
        [FromQuery] PageRequest page,
        CancellationToken cancellationToken)
    {
        var result = await queries.SearchPagedAsync(
            search,
            page,
            cancellationToken);

        return ApiResponse<PageResult<RasGateModel>>.Ok(result);
    }

    [HttpGet("all", Name = "SearchAllRasGates")]
    [EndpointSummary("Search all gateways")]
    [EndpointDescription(
        "Searches Hub-owned RasGate registrations by a case-insensitive literal substring and returns all matches without pagination. Supported fields are Name and Url; Name is searched when fields are omitted, and multiple fields are combined with OR. Stored API keys are never returned.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RasGateModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<IReadOnlyList<RasGateModel>>> SearchAll(
        [FromQuery] SearchRasGatesRequest search,
        CancellationToken cancellationToken)
    {
        var result = await queries.SearchAllAsync(
            search,
            cancellationToken);

        return ApiResponse<IReadOnlyList<RasGateModel>>.Ok(result);
    }
}
