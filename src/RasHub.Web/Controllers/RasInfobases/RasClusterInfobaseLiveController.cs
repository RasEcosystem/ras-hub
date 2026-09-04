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
using RasHub.Web.Api.RasEndpoints;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.RasGates;

namespace RasHub.Web.Controllers.RasInfobases;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route(
    "api/v1/ras-endpoints/{rasEndpointId:guid}/clusters/{clusterId:guid}/infobases")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("Infobases")]
[ControllerDescription("Infobases",
    "Inspect the persisted infobase shadow owned by a RAS endpoint and refresh it through the assigned RasGate.")]
public sealed class RasClusterInfobaseLiveController(
    ActiveRasEndpointLookup rasEndpointLookup,
    RasClusterQueries clusterQueries,
    RasInfobaseQueries infobaseQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost("live", Name = "GetLivePagedInfobases")]
    [EndpointSummary("Get paged live infobases")]
    [EndpointDescription(
        "Fetches the complete live infobase snapshot from the RAS endpoint through its assigned RasGate, atomically refreshes the persisted shadow, and returns one page from the updated shadow. Pagination limits only the HTTP response.")]
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
        Guid rasEndpointId,
        Guid clusterId,
        [FromQuery] PageRequest pageRequest,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        InfobaseCredentialsRequest? request,
        CancellationToken cancellationToken)
    {
        var state = await rasEndpointLookup.GetStateAsync(
            rasEndpointId,
            cancellationToken);

        if (state != ActiveRasEndpointState.Active)
            return RasEndpointApiResponses
                .ForUnavailableEndpoint<PageResult<InfobaseModel>>(
                    state,
                    rasEndpointId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasEndpointId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses
                .ClusterNotFound<PageResult<InfobaseModel>>(clusterId);

        var execution = await RefreshShadowAsync(
            rasEndpointId,
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
            rasEndpointId,
            clusterId,
            pageRequest,
            cancellationToken);

        return ApiResponse<PageResult<InfobaseModel>>.Ok(infobases);
    }

    [HttpPost("live/all", Name = "GetLiveAllInfobases")]
    [EndpointSummary("Get all live infobases")]
    [EndpointDescription(
        "Fetches the complete live infobase snapshot from the RAS endpoint through its assigned RasGate, atomically refreshes the persisted shadow, and returns the complete updated shadow without pagination.")]
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
        Guid rasEndpointId,
        Guid clusterId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        InfobaseCredentialsRequest? request,
        CancellationToken cancellationToken)
    {
        var state = await rasEndpointLookup.GetStateAsync(
            rasEndpointId,
            cancellationToken);

        if (state != ActiveRasEndpointState.Active)
            return RasEndpointApiResponses
                .ForUnavailableEndpoint<IReadOnlyList<InfobaseModel>>(
                    state,
                    rasEndpointId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasEndpointId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses
                .ClusterNotFound<IReadOnlyList<InfobaseModel>>(clusterId);

        var execution = await RefreshShadowAsync(
            rasEndpointId,
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
            rasEndpointId,
            clusterId,
            cancellationToken);

        return ApiResponse<IReadOnlyList<InfobaseModel>>.Ok(infobases);
    }

    [HttpPost("live/{infobaseId:guid}", Name = "GetLiveInfobase")]
    [EndpointSummary("Get live infobase")]
    [EndpointDescription(
        "Fetches one live infobase from the RAS endpoint through its assigned RasGate, refreshes that entry in the persisted shadow without changing siblings, and returns the updated shadow entry.")]
    [ProducesResponseType<ApiResponse<InfobaseModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<InfobaseModel>> GetLiveOne(
        Guid rasEndpointId,
        Guid clusterId,
        Guid infobaseId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        InfobaseCredentialsRequest? request,
        CancellationToken cancellationToken)
    {
        var state = await rasEndpointLookup.GetStateAsync(
            rasEndpointId,
            cancellationToken);

        if (state != ActiveRasEndpointState.Active)
            return RasEndpointApiResponses.ForUnavailableEndpoint<InfobaseModel>(
                state,
                rasEndpointId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasEndpointId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses.ClusterNotFound<InfobaseModel>(
                clusterId);

        var execution = await taskRunner.RunAsync(
            new SynchronizeInfobaseTask(
                rasEndpointId,
                clusterId,
                infobaseId,
                request?.ClusterUser,
                request?.ClusterPassword),
            RasGateTaskOptions.InteractiveInfobaseSynchronization(
                rasEndpointId,
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
            rasEndpointId,
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
        "Fetches the complete live infobase snapshot from the RAS endpoint through its assigned RasGate, atomically refreshes the persisted shadow, and returns refresh metadata without returning the collection.")]
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
            Guid rasEndpointId,
            Guid clusterId,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
            InfobaseCredentialsRequest? request,
            CancellationToken cancellationToken)
    {
        var state = await rasEndpointLookup.GetStateAsync(
            rasEndpointId,
            cancellationToken);

        if (state != ActiveRasEndpointState.Active)
            return RasEndpointApiResponses
                .ForUnavailableEndpoint<ShadowRefreshResponse>(
                    state,
                    rasEndpointId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasEndpointId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses
                .ClusterNotFound<ShadowRefreshResponse>(clusterId);

        var execution = await RefreshShadowAsync(
            rasEndpointId,
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
            Guid rasEndpointId,
            Guid clusterId,
            InfobaseCredentialsRequest? request,
            CancellationToken cancellationToken)
    {
        return taskRunner.RunWithResultAsync<
            SynchronizeInfobasesTask,
            CollectionSynchronizationResult>(
            new SynchronizeInfobasesTask(
                rasEndpointId,
                clusterId,
                request?.ClusterUser,
                request?.ClusterPassword),
            RasGateTaskOptions.InteractiveInfobasesSynchronization(
                rasEndpointId,
                clusterId),
            cancellationToken);
    }
}
