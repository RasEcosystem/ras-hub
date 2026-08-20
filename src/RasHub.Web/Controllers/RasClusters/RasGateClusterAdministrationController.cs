using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks.Clusters;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.Infrastructure.RasGates;
using ContractLoadBalancingMode = RasHub.Contracts.RasHub.Models.ClusterLoadBalancingMode;
using DomainLoadBalancingMode = RasHub.Domain.Enums.RasClusterLoadBalancingMode;

namespace RasHub.Web.Controllers.RasClusters;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-gates/{rasGateId:guid}/clusters")]
[Authorize(
    AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme,
    Policy = AppPolicies.ManageRasGates)]
[Tags("Clusters")]
[ControllerDescription("Clusters",
    "Inspect, synchronize, and manage 1C:Enterprise clusters through a registered gateway.")]
public sealed class RasGateClusterAdministrationController(
    ActiveRasGateLookup rasGateLookup,
    RasClusterQueries clusterQueries,
    InteractiveTaskRunner taskRunner) : ControllerBase
{
    [HttpPost(Name = "CreateCluster")]
    [EndpointSummary("Create cluster")]
    [EndpointDescription(
        "Creates a cluster through RAC, reads its authoritative state, and publishes that state to the local cache. Agent credentials are used only for this request.")]
    [ProducesResponseType<ApiResponse<ClusterModel>>(StatusCodes.Status201Created)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<ClusterModel>> CreateCluster(
        Guid rasGateId,
        [FromBody] CreateClusterRequest request,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(rasGateId, cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<ClusterModel>(
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
            return RasGateApiResponses.ClusterCreationNotConfirmed();

        var location = Url.Link(
            "GetCluster",
            new { rasGateId, clusterId });

        if (location is not null)
            Response.Headers.Location = location;

        return ApiResponse<ClusterModel>.Created(cluster);
    }

    [HttpPatch("{clusterId:guid}", Name = "UpdateCluster")]
    [EndpointSummary("Update cluster")]
    [EndpointDescription(
        "Updates cluster settings through RAC, reads the authoritative state, and publishes that state to the local cache. Agent credentials are used only for this request.")]
    [ProducesResponseType<ApiResponse<ClusterModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<ClusterModel>> UpdateCluster(
        Guid rasGateId,
        Guid clusterId,
        [FromBody] UpdateClusterRequest request,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(rasGateId, cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<ClusterModel>(
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
            ? RasGateApiResponses.ClusterUpdateNotConfirmed()
            : ApiResponse<ClusterModel>.Ok(cluster);
    }

    [HttpPost("{clusterId:guid}/remove", Name = "RemoveCluster")]
    [EndpointSummary("Remove cluster")]
    [EndpointDescription(
        "Removes a cluster through RAC and then removes it from the local shadow state. Optional cluster administrator credentials are used only for this request.")]
    [ProducesResponseType<ApiResponse<ClusterModel>>(StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout)]
    public async Task<ApiResponse<ClusterModel>> RemoveCluster(
        Guid rasGateId,
        Guid clusterId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        RemoveClusterRequest? request,
        CancellationToken cancellationToken)
    {
        var state = await rasGateLookup.GetStateAsync(
            rasGateId,
            cancellationToken);

        if (state != ActiveRasGateState.Active)
            return RasGateApiResponses.ForUnavailableGate<ClusterModel>(
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
            ? ApiResponse<ClusterModel>.Ok(cluster)
            : RasGateApiResponses.ClusterRemovalFailed(taskResult);
    }

    private static RasClusterCreationOptions Map(CreateClusterRequest request)
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

    private static RasClusterUpdateOptions Map(UpdateClusterRequest request)
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
