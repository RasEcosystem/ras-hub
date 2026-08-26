using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks.Infobases;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests.Infobases;
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
    "Inspect the persisted infobase shadow and refresh it from live RasGate data.")]
public sealed class RasClusterInfobaseLiveController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    RasInfobaseQueries infobaseQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost("live", Name = "GetLivePagedInfobases")]
    [EndpointSummary("Get paged live infobases")]
    [EndpointDescription(
        "Fetches the complete live infobase snapshot from RasGate, atomically refreshes the persisted shadow, and returns one page from the updated shadow. Pagination limits only the HTTP response; RasGate is queried for the complete snapshot.")]
    [ProducesResponseType<ApiResponse<PageResult<InfobaseModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<PageResult<InfobaseModel>>> GetLivePaged(
        Guid rasGateId,
        Guid clusterId,
        [FromQuery] PageRequest pageRequest,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        InfobaseCredentialsRequest? request,
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

        var execution = await RefreshShadowAsync(
            rasGateId,
            clusterId,
            request,
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses
                .InfobaseLiveRefreshRejected<PageResult<InfobaseModel>>();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses
                .InfobaseLiveRefreshFailed<PageResult<InfobaseModel>>(
                    taskResult);

        var infobases = await infobaseQueries.GetPagedAsync(
            rasGateId,
            clusterId,
            pageRequest,
            cancellationToken);

        return ApiResponse<PageResult<InfobaseModel>>.Ok(infobases);
    }

    [HttpPost("live/all", Name = "GetLiveAllInfobases")]
    [EndpointSummary("Get all live infobases")]
    [EndpointDescription(
        "Fetches the complete live infobase snapshot from RasGate, atomically refreshes the persisted shadow, and returns the complete updated shadow without pagination.")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<InfobaseModel>>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<IReadOnlyList<InfobaseModel>>> GetLiveAll(
        Guid rasGateId,
        Guid clusterId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        InfobaseCredentialsRequest? request,
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

        var execution = await RefreshShadowAsync(
            rasGateId,
            clusterId,
            request,
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses
                .InfobaseLiveRefreshRejected<IReadOnlyList<InfobaseModel>>();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses
                .InfobaseLiveRefreshFailed<IReadOnlyList<InfobaseModel>>(
                    taskResult);

        var infobases = await infobaseQueries.GetAllAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        return ApiResponse<IReadOnlyList<InfobaseModel>>.Ok(infobases);
    }

    [HttpPost("live/{infobaseId:guid}", Name = "GetLiveInfobase")]
    [EndpointSummary("Get live infobase")]
    [EndpointDescription(
        "Fetches one live infobase from RasGate, refreshes that entry in the persisted shadow without changing siblings, and returns the updated shadow entry.")]
    [ProducesResponseType<ApiResponse<InfobaseModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<InfobaseModel>> GetLiveOne(
        Guid rasGateId,
        Guid clusterId,
        Guid infobaseId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        InfobaseCredentialsRequest? request,
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
            return RasGateApiResponses.InfobaseLiveRefreshRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.InfobaseLiveRefreshFailed(
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

    [HttpPost("shadow/refresh", Name = "RefreshInfobaseShadow")]
    [EndpointSummary("Refresh infobase shadow")]
    [EndpointDescription(
        "Fetches the complete live infobase snapshot from RasGate, atomically refreshes the persisted shadow, and returns refresh metadata without returning the collection.")]
    [ProducesResponseType<ApiResponse<ShadowRefreshResponse>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<ShadowRefreshResponse>>
        RefreshShadow(
            Guid rasGateId,
            Guid clusterId,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
            InfobaseCredentialsRequest? request,
            CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses
                .ForUnavailableGate<ShadowRefreshResponse>(
                    state,
                    rasGateId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasGateId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses
                .ClusterNotFound<ShadowRefreshResponse>(clusterId);

        var execution = await RefreshShadowAsync(
            rasGateId,
            clusterId,
            request,
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses
                .InfobaseShadowRefreshRejected<ShadowRefreshResponse>();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses
                .InfobaseShadowRefreshFailed<ShadowRefreshResponse>(
                    taskResult);

        var result = execution.Value!;

        return ApiResponse<ShadowRefreshResponse>.Ok(
            new ShadowRefreshResponse { TotalCount = result.TotalCount, ObservedAt = result.ObservedAt });
    }

    private Task<InteractiveTaskExecution<CollectionSynchronizationResult>>
        RefreshShadowAsync(
            Guid rasGateId,
            Guid clusterId,
            InfobaseCredentialsRequest? request,
            CancellationToken cancellationToken)
    {
        return taskRunner.RunWithResultAsync<
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
    }
}
