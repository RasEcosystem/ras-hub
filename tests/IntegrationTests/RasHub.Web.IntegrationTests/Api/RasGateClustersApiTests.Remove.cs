using System.Net;
using System.Net.Http.Json;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Contracts.RasHub.Requests;
using static RasHub.Web.IntegrationTests.Api.ApiResponseTestHelpers;

namespace RasHub.Web.IntegrationTests.Api;

public sealed partial class RasGateClustersApiTests
{
    [Fact]
    public async Task Remove_deletes_remote_cluster_and_soft_deletes_shadow_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to remove");
        _factory.RasGateBoundary.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/remove",
            new RemoveClusterRequest(
                "cluster-admin",
                "cluster-secret"),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            cluster.ExternalId,
            json.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRemoveRequestCount);
        Assert.Equal(
            cluster.ExternalId,
            _factory.RasGateBoundary.RemovedClusterId);
        Assert.Equal(
            "cluster-admin",
            _factory.RasGateBoundary.LastClusterUser);
        Assert.Equal(
            "cluster-secret",
            _factory.RasGateBoundary.LastClusterPassword);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
        var stored = Assert.Single(await _factory.FindRasClustersAsync(
            rasGate.Id,
            true));
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAt);
    }

    [Fact]
    public async Task Remove_unknown_cluster_returns_not_found_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}/remove",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRemoveRequestCount);
    }

    [Fact]
    public async Task Remove_with_password_but_no_user_rejects_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}/remove",
            new RemoveClusterRequest(ClusterPassword: "cluster-secret"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRemoveRequestCount);
    }

    [Fact]
    public async Task Remove_when_RAC_fails_keeps_shadow_state_without_retry()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to keep");
        _factory.RasGateBoundary.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateBoundary.ClusterRemoveException =
            new RasGateClientException("Remove failed.");

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/remove",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "cluster_remove_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRemoveRequestCount);
        Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Remove_when_outcome_is_unknown_keeps_shadow_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to keep");
        _factory.RasGateBoundary.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateBoundary.ClusterRemoveException =
            new RasGateMutationOutcomeUnknownException(
                rasGate.Id,
                "clusters",
                "remove");

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/remove",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "cluster_remove_outcome_unknown",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRemoveRequestCount);
        Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Remove_when_configuration_changes_after_remote_removal_returns_not_confirmed_without_retry()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to keep");
        _factory.RasGateBoundary.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();
        using var configurationClient = _factory.CreateAuthenticatedClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateBoundary.PauseClusterPublications();
        var mutationRequest = client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/" +
            $"{cluster.ExternalId}/remove",
            null,
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
        AssertMutationNotConfirmed(json, "cluster_remove_not_confirmed");
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRemoveRequestCount);
        Assert.Equal(cluster.ExternalId, _factory.RasGateBoundary.RemovedClusterId);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
        var stored = Assert.Single(await _factory.FindRasClustersAsync(
            rasGate.Id,
            true));
        Assert.Equal("Cluster to keep", stored.Name);
        Assert.True(stored.IsDeleted);
    }

    [Fact]
    public async Task Remove_without_capability_keeps_shadow_state_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to keep");
        _factory.RasGateBoundary.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateBoundary.SupportsClusterRemove = false;

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/remove",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("rac_capability_not_supported", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRemoveRequestCount);
        Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Remove_from_inactive_gate_returns_conflict_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync(isActive: false);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}/remove",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRemoveRequestCount);
    }
}
