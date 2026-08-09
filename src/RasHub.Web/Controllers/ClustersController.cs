using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.RasGates.Tasks;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Synchronization.Abstractions;
using RasHub.Synchronization.Exceptions;
using RasHub.Synchronization.Models;
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
        "Returns cached clusters. Set refresh=true to synchronize them with the gateway first.")]
    [ProducesResponseType(
        typeof(ApiResponse<PageResult<RasClusterModel>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<PageResult<RasClusterModel>>> GetPaged(
        Guid rasGateId,
        [FromBody] PageRequest request,
        [FromServices] RasGateQueries rasGateQueries,
        [FromServices] RasClusterQueries clusterQueries,
        [FromServices] ISynchronizationEngine synchronizationEngine,
        CancellationToken cancellationToken,
        [FromQuery] bool refresh = false)
    {
        var rasGate = await rasGateQueries.GetByIdAsync(
            rasGateId,
            cancellationToken);

        if (rasGate is null)
            return CreateNotFoundResponse(rasGateId);

        if (refresh)
        {
            BackgroundTaskHandle handle;

            try
            {
                handle = synchronizationEngine.Enqueue(
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
                return CreateSynchronizationFailureResponse(synchronizationResult);
        }

        var result = await clusterQueries.GetPagedAsync(
            rasGateId,
            request,
            cancellationToken);

        return ApiResponse<PageResult<RasClusterModel>>.Ok(result);
    }

    private static ApiResponse<PageResult<RasClusterModel>> CreateNotFoundResponse(
        Guid rasGateId)
    {
        return ApiResponse<PageResult<RasClusterModel>>.Fail(
            HttpStatusCode.NotFound,
            "ras_gate_not_found",
            $"RasGate '{rasGateId}' was not found.");
    }

    private static ApiResponse<PageResult<RasClusterModel>>
        CreateSynchronizationFailureResponse(BackgroundTaskResult result)
    {
        if (result.Outcome == BackgroundTaskOutcome.Canceled)
            return ApiResponse<PageResult<RasClusterModel>>.Fail(
                HttpStatusCode.ServiceUnavailable,
                "ras_gate_clusters_sync_canceled",
                "RasGate cluster synchronization was canceled.");

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
