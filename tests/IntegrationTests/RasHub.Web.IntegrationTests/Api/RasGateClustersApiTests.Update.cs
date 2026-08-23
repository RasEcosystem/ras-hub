using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Domain.Enums;
using RasHub.Web.IntegrationTests.Infrastructure;
using static RasHub.Web.IntegrationTests.Api.ApiResponseTestHelpers;

namespace RasHub.Web.IntegrationTests.Api;


public sealed partial class RasGateClustersApiTests
{
    [Fact]
    public async Task Update_publishes_and_returns_authoritative_cluster()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var initial = CreateSnapshot(Guid.NewGuid(), "Old cluster");
        _factory.RasGateBoundary.Clusters = [initial];
        using var client = _factory.CreateAuthenticatedClient();
        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        var updated = CreateSnapshot(initial.ExternalId, "Updated cluster");
        _factory.RasGateBoundary.Cluster = updated;
        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{initial.ExternalId}",
            new UpdateClusterRequest(
                "Updated cluster",
                AgentUser: "agent-admin",
                AgentPassword: "agent-secret"),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Updated cluster", json.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterUpdateRequestCount);
        Assert.Equal(1, _factory.RasGateBoundary.ClusterInfoRequestCount);
        Assert.Equal(initial.ExternalId, _factory.RasGateBoundary.UpdatedClusterId);
        var options = Assert.IsType<RasClusterUpdateOptions>(
            _factory.RasGateBoundary.LastClusterUpdateOptions);
        Assert.Equal("agent-admin", options.AgentUser);
        Assert.Equal("agent-secret", options.AgentPassword);
        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal("Updated cluster", stored.Name);
    }

    [Fact]
    public async Task Update_when_RAC_fails_keeps_shadow_state_without_retry()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var initial = CreateSnapshot(Guid.NewGuid(), "Old cluster");
        _factory.RasGateBoundary.Clusters = [initial];
        using var client = _factory.CreateAuthenticatedClient();
        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);
        _factory.RasGateBoundary.ClusterUpdateException =
            new RasGateClientException("Update failed.");

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{initial.ExternalId}",
            new UpdateClusterRequest("Updated cluster"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, _factory.RasGateBoundary.ClusterUpdateRequestCount);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterInfoRequestCount);
        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal("Old cluster", stored.Name);
    }

    [Theory]
    [InlineData("not-found")]
    [InlineData("protocol")]
    [InlineData("rac-unavailable")]
    [InlineData("timeout")]
    [InlineData("canceled")]
    public async Task Update_when_remote_read_back_fails_returns_not_confirmed_and_keeps_shadow_state(
        string failure)
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var initial = CreateSnapshot(Guid.NewGuid(), "Old cluster");
        _factory.RasGateBoundary.Clusters = [initial];
        using var client = _factory.CreateAuthenticatedClient();
        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);
        _factory.RasGateBoundary.ClusterException =
            CreateReadBackException(failure, rasGate.Id, initial.ExternalId);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{initial.ExternalId}",
            new UpdateClusterRequest("Updated cluster"),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("cluster_update_not_confirmed", GetErrorCode(json));
        Assert.Contains("Refresh", json.ToString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterUpdateRequestCount);
        Assert.Equal(1, _factory.RasGateBoundary.ClusterInfoRequestCount);
        Assert.Equal(initial.ExternalId, _factory.RasGateBoundary.UpdatedClusterId);
        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal("Old cluster", stored.Name);
    }

    [Fact]
    public async Task
        Update_when_configuration_changes_after_remote_read_back_returns_not_confirmed_and_keeps_old_shadow()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var initial = CreateSnapshot(Guid.NewGuid(), "Old cluster");
        _factory.RasGateBoundary.Clusters = [initial];
        using var client = _factory.CreateAuthenticatedClient();
        using var configurationClient = _factory.CreateAuthenticatedClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateBoundary.Cluster = CreateSnapshot(
            initial.ExternalId,
            "Updated cluster");
        _factory.RasGateBoundary.PauseClusterPublications();
        var mutationRequest = client.PatchAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{initial.ExternalId}",
            new UpdateClusterRequest("Updated cluster"),
            cancellationToken);
        await _factory.RasGateBoundary.WaitForClusterPublicationAsync(
            cancellationToken);

        try
        {
            await ReconfigureRasGateAsync(
                configurationClient,
                rasGate.Id,
                rasGate.Name,
                cancellationToken);
        }
        finally
        {
            _factory.RasGateBoundary.ReleaseClusterPublications();
        }

        using var response = await mutationRequest;
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertMutationNotConfirmed(json, "cluster_update_not_confirmed");
        Assert.Equal(1, _factory.RasGateBoundary.ClusterUpdateRequestCount);
        Assert.Equal(1, _factory.RasGateBoundary.ClusterInfoRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
        var stored = Assert.Single(await _factory.FindRasClustersAsync(
            rasGate.Id,
            true));
        Assert.Equal("Old cluster", stored.Name);
        Assert.True(stored.IsDeleted);
    }

    [Fact]
    public async Task Update_when_outcome_is_unknown_keeps_shadow_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var initial = CreateSnapshot(Guid.NewGuid(), "Old cluster");
        _factory.RasGateBoundary.Clusters = [initial];
        using var client = _factory.CreateAuthenticatedClient();
        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);
        _factory.RasGateBoundary.ClusterUpdateException =
            new RasGateMutationOutcomeUnknownException(
                rasGate.Id,
                "clusters",
                "update");

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{initial.ExternalId}",
            new UpdateClusterRequest("Updated cluster"),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "cluster_update_outcome_unknown",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterUpdateRequestCount);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterInfoRequestCount);
        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal("Old cluster", stored.Name);
    }

    [Fact]
    public async Task Update_unknown_cluster_returns_not_found_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{clusterId}",
            new UpdateClusterRequest("Updated cluster"),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("cluster_not_found", GetErrorCode(json));
        Assert.Equal(
            $"Cluster '{clusterId}' was not found.",
            json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(0, _factory.RasGateBoundary.ClusterUpdateRequestCount);
    }

    [Fact]
    public async Task Update_without_settings_rejects_request_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}",
            new UpdateClusterRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterUpdateRequestCount);
    }
}
