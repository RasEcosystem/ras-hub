using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.Infrastructure.RasGates;
using ContractLoadBalancingMode = RasHub.Contracts.RasHub.Models.RasClusterLoadBalancingMode;
using DomainLoadBalancingMode = RasHub.Domain.Enums.RasClusterLoadBalancingMode;

namespace RasHub.Web.Controllers;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates/{rasGateId:guid}/clusters")]
[Authorize(
    AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme,
    Policy = AppPolicies.ManageRasGates)]
[ControllerDescription(
    "Manage 1C:Enterprise clusters through a registered gateway.")]
public sealed class RasGateClusterAdministrationController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost]
    [EndpointSummary("Create cluster")]
    [EndpointDescription(
        "Creates a cluster through RAC, reads its authoritative state, and publishes that state to the local cache. Agent credentials are used only for this request.")]
    [ProducesResponseType(
        typeof(ApiResponse<RasClusterModel>),
        StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<RasClusterModel>> Create(
        Guid rasGateId,
        [FromBody] CreateRasClusterRequest request,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(rasGateId, cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<RasClusterModel>(
                state,
                rasGateId);

        var execution = await taskRunner.RunWithResultAsync<
            CreateClusterTask,
            Guid>(
            new CreateClusterTask(rasGateId, Map(request)),
            RasGateTaskOptions.InteractiveClusterCreation(rasGateId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.ClusterCreationRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.ClusterCreationFailed(taskResult);

        var clusterId = execution.Value;
        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        if (cluster is null)
            return RasGateApiResponses.ClusterCreationNotPublished();

        Response.Headers.Location =
            $"/api/v1/ras-gates/{rasGateId:D}/clusters/{clusterId:D}";
        return ApiResponse<RasClusterModel>.Created(cluster);
    }

    [HttpPut("{clusterId:guid}")]
    [EndpointSummary("Update cluster")]
    [EndpointDescription(
        "Updates cluster settings through RAC, reads the authoritative state, and publishes that state to the local cache. Agent credentials are used only for this request.")]
    [ProducesResponseType(typeof(ApiResponse<RasClusterModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(OpenApiErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<RasClusterModel>> Update(
        Guid rasGateId,
        Guid clusterId,
        [FromBody] UpdateRasClusterRequest request,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(rasGateId, cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<RasClusterModel>(
                state,
                rasGateId);

        if (await clusterQueries.GetByExternalIdAsync(
                rasGateId,
                clusterId,
                cancellationToken) is null)
            return RasGateApiResponses.ClusterNotFound(clusterId);

        var execution = await taskRunner.RunAsync(
            new UpdateClusterTask(rasGateId, clusterId, Map(request)),
            RasGateTaskOptions.InteractiveClusterUpdate(rasGateId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.ClusterUpdateRejected();

        var taskResult = execution.Result!;

        if (!taskResult.IsSucceeded)
            return RasGateApiResponses.ClusterUpdateFailed(taskResult);

        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        return cluster is null
            ? RasGateApiResponses.ClusterNotFound(clusterId)
            : ApiResponse<RasClusterModel>.Ok(cluster);
    }

    [HttpDelete("{clusterId:guid}")]
    [EndpointSummary("Remove cluster")]
    [EndpointDescription(
        "Removes a cluster through RAC and then removes it from the local shadow state. Optional cluster administrator credentials are used only for this request.")]
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
    public async Task<ApiResponse<RasClusterModel>> Remove(
        Guid rasGateId,
        Guid clusterId,
        [FromBody] RemoveRasClusterRequest? request,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<RasClusterModel>(
                state,
                rasGateId);

        var cluster = await clusterQueries.GetByExternalIdAsync(
            rasGateId,
            clusterId,
            cancellationToken);

        if (cluster is null)
            return RasGateApiResponses.ClusterNotFound(clusterId);

        var execution = await taskRunner.RunAsync(
            new RemoveClusterTask(
                rasGateId,
                clusterId,
                request?.ClusterUser,
                request?.ClusterPassword),
            RasGateTaskOptions.InteractiveClusterRemoval(
                rasGateId,
                clusterId),
            cancellationToken);

        if (execution.WasRejected)
            return RasGateApiResponses.ClusterRemovalRejected();

        var taskResult = execution.Result!;

        return taskResult.IsSucceeded
            ? ApiResponse<RasClusterModel>.Ok(cluster)
            : RasGateApiResponses.ClusterRemovalFailed(taskResult);
    }

    private static RasClusterCreationOptions Map(CreateRasClusterRequest request)
    {
        return new RasClusterCreationOptions
        {
            Host = request.Host,
            Port = request.Port,
            Name = request.Name,
            ExpirationTimeoutSeconds = request.ExpirationTimeoutSeconds,
            LifetimeLimitSeconds = request.LifetimeLimitSeconds,
            MaxMemorySizeKb = request.MaxMemorySizeKb,
            MaxMemoryTimeLimitSeconds = request.MaxMemoryTimeLimitSeconds,
            SecurityLevel = request.SecurityLevel,
            SessionFaultToleranceLevel = request.SessionFaultToleranceLevel,
            LoadBalancingMode = Map(request.LoadBalancingMode),
            ErrorsCountThresholdPercent = request.ErrorsCountThresholdPercent,
            KillProblemProcesses = request.KillProblemProcesses,
            AgentUser = request.AgentUser,
            AgentPassword = request.AgentPassword
        };
    }

    private static RasClusterUpdateOptions Map(UpdateRasClusterRequest request)
    {
        return new RasClusterUpdateOptions
        {
            Name = request.Name,
            ExpirationTimeoutSeconds = request.ExpirationTimeoutSeconds,
            LifetimeLimitSeconds = request.LifetimeLimitSeconds,
            MaxMemorySizeKb = request.MaxMemorySizeKb,
            MaxMemoryTimeLimitSeconds = request.MaxMemoryTimeLimitSeconds,
            SecurityLevel = request.SecurityLevel,
            SessionFaultToleranceLevel = request.SessionFaultToleranceLevel,
            LoadBalancingMode = Map(request.LoadBalancingMode),
            ErrorsCountThresholdPercent = request.ErrorsCountThresholdPercent,
            KillProblemProcesses = request.KillProblemProcesses,
            AgentUser = request.AgentUser,
            AgentPassword = request.AgentPassword
        };
    }

    private static DomainLoadBalancingMode? Map(
        ContractLoadBalancingMode? value)
    {
        return value switch
        {
            ContractLoadBalancingMode.Performance => DomainLoadBalancingMode.Performance,
            ContractLoadBalancingMode.Memory => DomainLoadBalancingMode.Memory,
            null => null,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }
}