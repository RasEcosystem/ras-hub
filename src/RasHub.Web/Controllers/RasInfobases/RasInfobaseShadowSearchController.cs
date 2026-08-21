using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models.Search;
using RasHub.Contracts.RasHub.Requests.Search;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers.RasInfobases;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/infobases/shadow/search")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("Infobases")]
[ControllerDescription("Infobases",
    "Inspect the persisted infobase shadow and refresh it from live RasGate data.")]
public sealed class RasInfobaseShadowSearchController(
    RasInfobaseQueries queries) : ControllerBase
{
    [HttpGet(Name = "SearchShadowPagedInfobases")]
    [EndpointSummary("Search paged infobase shadow")]
    [EndpointDescription(
        "Searches the persisted infobase shadow across all RasGates and clusters by a case-insensitive literal substring and returns one page. Supported fields are Name and Description; Name is searched when fields are omitted, and multiple fields are combined with OR. Optional RasGate and cluster filters narrow the scope; a cluster filter requires its RasGate filter. Every result includes the IDs and names of its RasGate and cluster. RasGate is not contacted.")]
    [ProducesResponseType<ApiResponse<PageResult<InfobaseSearchResultModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<PageResult<InfobaseSearchResultModel>>>
        SearchPaged(
            [FromQuery] SearchInfobasesRequest search,
            [FromQuery] PageRequest page,
            CancellationToken cancellationToken)
    {
        var result = await queries.SearchPagedAsync(
            search,
            page,
            cancellationToken);

        return ApiResponse<PageResult<InfobaseSearchResultModel>>.Ok(result);
    }

    [HttpGet("all", Name = "SearchShadowAllInfobases")]
    [EndpointSummary("Search all infobase shadow entries")]
    [EndpointDescription(
        "Searches the persisted infobase shadow across all RasGates and clusters by a case-insensitive literal substring and returns all matches without pagination. Supported fields are Name and Description; Name is searched when fields are omitted, and multiple fields are combined with OR. Optional RasGate and cluster filters narrow the scope; a cluster filter requires its RasGate filter. Every result includes the IDs and names of its RasGate and cluster. RasGate is not contacted.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<InfobaseSearchResultModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<IReadOnlyList<InfobaseSearchResultModel>>>
        SearchAll(
            [FromQuery] SearchInfobasesRequest search,
            CancellationToken cancellationToken)
    {
        var result = await queries.SearchAllAsync(
            search,
            cancellationToken);

        return ApiResponse<IReadOnlyList<InfobaseSearchResultModel>>.Ok(result);
    }
}
