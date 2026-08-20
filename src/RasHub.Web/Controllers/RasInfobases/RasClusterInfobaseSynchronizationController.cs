using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks.Infobases;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Contracts.RasHub.Responses;
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
[Tags("Infobases")]
[ControllerDescription("Infobases",
    "Inspect and synchronize 1C:Enterprise infobases registered in a cluster.")]
public sealed class RasClusterInfobaseSynchronizationController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    RasInfobaseQueries infobaseQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost("synchronize", Name = "SynchronizeInfobases")]
    [EndpointSummary("Synchronize infobases")]
    [EndpointDescription(
        "Synchronizes the complete RAC infobase summary list, persists it, and returns synchronization metadata.")]
    [ProducesResponseType<ApiResponse<CollectionSynchronizationResponse>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<CollectionSynchronizationResponse>> SynchronizeInfobases(
        Guid rasGateId,
        Guid clusterId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        SynchronizeInfobasesRequest? request,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses
                .ForUnavailableGate<CollectionSynchronizationResponse>(
                    state,
                    rasGateId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasGateId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses
                .ClusterNotFound<CollectionSynchronizationResponse>(clusterId);

        var execution = await taskRunner.RunWithResultAsync<
            SynchronizeInfobasesTask,
            CollectionSynchronizationResult>(
            new SynchronizeInfobasesTask(
                rasGateId,
                clusterId,
                request?.ClusterUser,
                request?.ClusterPassword),
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

        var result = execution.Value!;

        return ApiResponse<CollectionSynchronizationResponse>.Ok(
            new CollectionSynchronizationResponse { TotalCount = result.TotalCount, ObservedAt = result.ObservedAt });
    }

    [HttpPost("{infobaseId:guid}/synchronize", Name = "SynchronizeInfobase")]
    [EndpointSummary("Synchronize infobase")]
    [EndpointDescription(
        "Synchronizes one RAC infobase summary without changing sibling infobases.")]
    [ProducesResponseType<ApiResponse<InfobaseModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<InfobaseModel>> SynchronizeInfobase(
        Guid rasGateId,
        Guid clusterId,
        Guid infobaseId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        SynchronizeInfobaseRequest? request,
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
