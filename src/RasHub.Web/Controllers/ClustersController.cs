using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Tasks;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.BackgroundTasks.Exceptions;
using RasHub.BackgroundTasks.Models;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates/{rasGateId:guid}/clusters")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[ControllerDescription(
    "Inspect 1C:Enterprise clusters available through a registered gateway.")]
public sealed class ClustersController : ControllerBase
{
    [HttpPost("get-paged")]
    [EndpointSummary("List clusters")]
    [EndpointDescription(
        "Returns cached clusters without contacting the gateway.")]
    [ProducesResponseType(
        typeof(ApiResponse<PageResult<RasClusterModel>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<PageResult<RasClusterModel>>> GetPaged(
        Guid rasGateId,
        [FromBody] PageRequest request,
        [FromServices] RasGateQueries rasGateQueries,
        [FromServices] RasClusterQueries clusterQueries,
        CancellationToken cancellationToken)
    {
        var rasGate = await rasGateQueries.GetByIdAsync(
            rasGateId,
            cancellationToken);

        if (rasGate is null)
            return CreateNotFoundResponse<PageResult<RasClusterModel>>(rasGateId);

        if (!rasGate.IsActive)
            return CreateInactiveResponse<PageResult<RasClusterModel>>(rasGateId);

        var result = await clusterQueries.GetPagedAsync(
            rasGateId,
            request,
            cancellationToken);

        return ApiResponse<PageResult<RasClusterModel>>.Ok(result);
    }

    [HttpGet("{clusterId:guid}")]
    [EndpointSummary("Get cluster")]
    [EndpointDescription(
        "Returns a cached cluster without contacting the gateway.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasClusterModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<RasClusterModel>> GetById(
        Guid rasGateId,
        Guid clusterId,
        [FromServices] RasGateQueries rasGateQueries,
        [FromServices] RasClusterQueries clusterQueries,
        CancellationToken cancellationToken)
    {
        var rasGate = await rasGateQueries.GetByIdAsync(
            rasGateId,
            cancellationToken);

        if (rasGate is null)
            return CreateNotFoundResponse<RasClusterModel>(rasGateId);

        if (!rasGate.IsActive)
            return CreateInactiveResponse<RasClusterModel>(rasGateId);

        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        return cluster is null
            ? CreateClusterNotFoundResponse(clusterId)
            : ApiResponse<RasClusterModel>.Ok(cluster);
    }

    [HttpPost("{clusterId:guid}/synchronize")]
    [EndpointSummary("Synchronize cluster")]
    [EndpointDescription(
        "Synchronizes one cluster through RAC cluster info, persists it, and returns the synchronized cluster.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasClusterModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<RasClusterModel>> SynchronizeById(
        Guid rasGateId,
        Guid clusterId,
        [FromServices] RasGateQueries rasGateQueries,
        [FromServices] RasClusterQueries clusterQueries,
        [FromServices] IBackgroundTaskEngine backgroundTaskEngine,
        CancellationToken cancellationToken)
    {
        var rasGate = await rasGateQueries.GetByIdAsync(
            rasGateId,
            cancellationToken);

        if (rasGate is null)
            return CreateNotFoundResponse<RasClusterModel>(rasGateId);

        if (!rasGate.IsActive)
            return CreateInactiveResponse<RasClusterModel>(rasGateId);

        BackgroundTaskHandle handle;

        try
        {
            handle = backgroundTaskEngine.Enqueue(
                new SynchronizeClusterTask(rasGateId, clusterId),
                new BackgroundTaskOptions
                {
                    Queue = BackgroundTaskQueue.Interactive,
                    MaxAttempts = 2,
                    RetryDelay = TimeSpan.FromMilliseconds(250),
                    Timeout = TimeSpan.FromSeconds(30),
                    DeduplicationKey =
                        $"ras-gate-cluster:{rasGateId}:{clusterId}",
                    ConcurrencyKey = $"ras-gate:{rasGateId}"
                });
        }
        catch (BackgroundTaskRejectedException)
        {
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_cluster_sync_unavailable",
                "RasGate cluster synchronization could not be scheduled.");
        }

        var synchronizationResult = await handle.WaitAsync(cancellationToken);

        if (!synchronizationResult.IsSucceeded)
            return CreateClusterSynchronizationFailureResponse(
                synchronizationResult);

        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        return cluster is null
            ? CreateClusterNotFoundResponse(clusterId)
            : ApiResponse<RasClusterModel>.Ok(cluster);
    }

    [HttpPost("synchronize")]
    [EndpointSummary("Synchronize clusters")]
    [EndpointDescription(
        "Synchronizes clusters with the gateway, persists the snapshot, and returns the requested synchronized page.")]
    [ProducesResponseType(
        typeof(ApiResponse<PageResult<RasClusterModel>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<PageResult<RasClusterModel>>> Synchronize(
        Guid rasGateId,
        [FromBody] PageRequest request,
        [FromServices] RasGateQueries rasGateQueries,
        [FromServices] RasClusterQueries clusterQueries,
        [FromServices] IBackgroundTaskEngine backgroundTaskEngine,
        CancellationToken cancellationToken)
    {
        var rasGate = await rasGateQueries.GetByIdAsync(
            rasGateId,
            cancellationToken);

        if (rasGate is null)
            return CreateNotFoundResponse<PageResult<RasClusterModel>>(rasGateId);

        if (!rasGate.IsActive)
            return CreateInactiveResponse<PageResult<RasClusterModel>>(rasGateId);

        BackgroundTaskHandle handle;

        try
        {
            handle = backgroundTaskEngine.Enqueue(
                new SynchronizeClustersTask(rasGateId),
                new BackgroundTaskOptions
                {
                    Queue = BackgroundTaskQueue.Interactive,
                    MaxAttempts = 2,
                    RetryDelay = TimeSpan.FromMilliseconds(250),
                    Timeout = TimeSpan.FromSeconds(30),
                    DeduplicationKey = $"ras-gate-clusters:{rasGateId}",
                    ConcurrencyKey = $"ras-gate:{rasGateId}"
                });
        }
        catch (BackgroundTaskRejectedException)
        {
            return ApiResponse<PageResult<RasClusterModel>>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_clusters_sync_unavailable",
                "RasGate cluster synchronization could not be scheduled.");
        }

        var synchronizationResult = await handle.WaitAsync(cancellationToken);

        if (!synchronizationResult.IsSucceeded)
            return CreateClustersSynchronizationFailureResponse(
                synchronizationResult);

        var result = await clusterQueries.GetPagedAsync(
            rasGateId,
            request,
            cancellationToken);

        return ApiResponse<PageResult<RasClusterModel>>.Ok(result);
    }

    private static ApiResponse<T> CreateNotFoundResponse<T>(
        Guid rasGateId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.NotFound,
            "ras_gate_not_found",
            $"RasGate '{rasGateId}' was not found.");
    }

    private static ApiResponse<T> CreateInactiveResponse<T>(
        Guid rasGateId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.Conflict,
            "ras_gate_inactive",
            $"RasGate '{rasGateId}' is inactive.");
    }

    private static ApiResponse<RasClusterModel> CreateClusterNotFoundResponse(
        Guid clusterId)
    {
        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.NotFound,
            "ras_cluster_not_found",
            $"RasCluster '{clusterId}' was not found.");
    }

    private static ApiResponse<RasClusterModel>
        CreateClusterSynchronizationFailureResponse(
            BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_cluster_sync_canceled",
                "RasGate cluster synchronization was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return CreateInactiveResponse<RasClusterModel>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during cluster synchronization.");

        if (result.Exception is TimeoutException)
            return ApiResponse<RasClusterModel>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_cluster_timeout",
                "RasGate cluster synchronization timed out.");

        return ApiResponse<RasClusterModel>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_cluster_sync_failed",
            "RasGate cluster could not be synchronized.");
    }

    private static ApiResponse<PageResult<RasClusterModel>>
        CreateClustersSynchronizationFailureResponse(
            BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<PageResult<RasClusterModel>>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_clusters_sync_canceled",
                "RasGate cluster synchronization was canceled.");

        if (result.Exception is RasGateInactiveException inactiveException)
            return CreateInactiveResponse<PageResult<RasClusterModel>>(
                inactiveException.RasGateId);

        if (result.Exception is RasGateConfigurationChangedException)
            return ApiResponse<PageResult<RasClusterModel>>.Fail(
                HttpStatusCode.Conflict,
                "ras_gate_configuration_changed",
                "RasGate configuration changed during cluster synchronization.");

        if (result.Exception is TimeoutException)
            return ApiResponse<PageResult<RasClusterModel>>.Fail(
                HttpStatusCode.GatewayTimeout,
                "ras_gate_clusters_timeout",
                "RasGate cluster synchronization timed out.");

        return ApiResponse<PageResult<RasClusterModel>>.Fail(
            HttpStatusCode.BadGateway,
            "ras_gate_clusters_sync_failed",
            "RasGate clusters could not be synchronized.");
    }
}
