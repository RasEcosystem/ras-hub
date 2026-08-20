using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.RasGates.Tasks.Infobases;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.RasGates;

namespace RasHub.Web.Controllers.RasInfobases;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route(
    "api/v1/ras-gates/{rasGateId:guid}/clusters/{clusterId:guid}/infobases")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[ControllerDescription(
    "Synchronize 1C:Enterprise infobases registered in a cluster.")]
public sealed class RasClusterInfobaseSynchronizationController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    RasInfobaseQueries infobaseQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost("synchronize")]
    [EndpointSummary("Synchronize infobases")]
    [EndpointDescription(
        "Synchronizes the complete RAC infobase summary list, persists it, and returns the requested page.")]
    [ProducesResponseType(
        typeof(ApiResponse<PageResult<InfobaseModel>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<PageResult<InfobaseModel>>> Synchronize(
        Guid rasGateId,
        Guid clusterId,
        [FromBody] SynchronizeInfobasesRequest request,
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

        var execution = await taskRunner.RunAsync(
            new SynchronizeInfobasesTask(
                rasGateId,
                clusterId,
                request.ClusterUser,
                request.ClusterPassword),
            RasGateTaskOptions.InteractiveInfobasesSynchronization(
                rasGateId,
                clusterId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.InfobasesSynchronizationRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.InfobasesSynchronizationFailed(
                taskResult);

        var result = await infobaseQueries.GetPagedAsync(
            rasGateId,
            clusterId,
            new PageRequest(request.Page, request.PageSize),
            cancellationToken);

        return ApiResponse<PageResult<InfobaseModel>>.Ok(result);
    }

    [HttpPost("{infobaseId:guid}/synchronize")]
    [EndpointSummary("Synchronize infobase")]
    [EndpointDescription(
        "Synchronizes one RAC infobase summary without changing sibling infobases.")]
    [ProducesResponseType(
        typeof(ApiResponse<InfobaseModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<InfobaseModel>> SynchronizeById(
        Guid rasGateId,
        Guid clusterId,
        Guid infobaseId,
        [FromBody] SynchronizeInfobaseRequest? request,
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

        var execution = await taskRunner.RunAsync(
            new SynchronizeInfobaseTask(
                rasGateId,
                clusterId,
                infobaseId,
                request?.ClusterUser,
                request?.ClusterPassword),
            RasGateTaskOptions.InteractiveInfobaseSynchronization(
                rasGateId,
                clusterId,
                infobaseId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.InfobaseSynchronizationRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.InfobaseSynchronizationFailed(
                taskResult);

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