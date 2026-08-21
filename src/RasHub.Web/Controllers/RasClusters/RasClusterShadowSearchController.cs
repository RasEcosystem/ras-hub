using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models.Search;
using RasHub.Contracts.RasHub.Requests.Search;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers.RasClusters;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/clusters/shadow/search")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("Clusters")]
[ControllerDescription("Clusters",
    "Manage 1C:Enterprise clusters, inspect their persisted shadow, and refresh it from live RasGate data.")]
public sealed class RasClusterShadowSearchController(
    RasClusterQueries queries) : ControllerBase
{
    [HttpGet(Name = "SearchShadowPagedClusters")]
    [EndpointSummary("Search paged cluster shadow")]
    [EndpointDescription(
        "Searches the persisted cluster shadow across all RasGates by a case-insensitive literal substring and returns one page. Supported fields are Name and Host; Name is searched when fields are omitted, and multiple fields are combined with OR. An optional RasGate filter narrows the scope. Every result includes its RasGate ID and name. RasGate is not contacted.")]
    [ProducesResponseType<ApiResponse<PageResult<ClusterSearchResultModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<PageResult<ClusterSearchResultModel>>>
        SearchPaged(
            [FromQuery] SearchClustersRequest search,
            [FromQuery] PageRequest page,
            CancellationToken cancellationToken)
    {
        var result = await queries.SearchPagedAsync(
            search,
            page,
            cancellationToken);

        return ApiResponse<PageResult<ClusterSearchResultModel>>.Ok(result);
    }

    [HttpGet("all", Name = "SearchShadowAllClusters")]
    [EndpointSummary("Search all cluster shadow entries")]
    [EndpointDescription(
        "Searches the persisted cluster shadow across all RasGates by a case-insensitive literal substring and returns all matches without pagination. Supported fields are Name and Host; Name is searched when fields are omitted, and multiple fields are combined with OR. An optional RasGate filter narrows the scope. Every result includes its RasGate ID and name. RasGate is not contacted.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ClusterSearchResultModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<IReadOnlyList<ClusterSearchResultModel>>>
        SearchAll(
            [FromQuery] SearchClustersRequest search,
            CancellationToken cancellationToken)
    {
        var result = await queries.SearchAllAsync(
            search,
            cancellationToken);

        return ApiResponse<IReadOnlyList<ClusterSearchResultModel>>.Ok(result);
    }
}
