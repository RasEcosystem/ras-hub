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
    public async Task Create_publishes_and_returns_authoritative_cluster()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        var snapshot = CreateSnapshot(clusterId, "Новый кластер");
        _factory.RasGateBoundary.CreatedClusterId = clusterId;
        _factory.RasGateBoundary.Cluster = snapshot;
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateClusterRequest(
                "localhost",
                1587,
                "Новый кластер",
                AgentUser: "agent-admin",
                AgentPassword: "agent-secret"),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(clusterId, json.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(
            $"/api/v1/ras-gates/{rasGate.Id:D}/clusters/shadow/{clusterId:D}",
            response.Headers.Location?.AbsolutePath);
        Assert.Equal(1, _factory.RasGateBoundary.ClusterCreateRequestCount);
        Assert.Equal(1, _factory.RasGateBoundary.ClusterInfoRequestCount);
        var options = Assert.IsType<RasClusterCreationOptions>(
            _factory.RasGateBoundary.LastClusterCreationOptions);
        Assert.Equal("localhost", options.Host);
        Assert.Equal(1587, options.Port);
        Assert.Equal("agent-admin", options.AgentUser);
        Assert.Equal("agent-secret", options.AgentPassword);
        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal(clusterId, stored.ExternalId);
        Assert.Equal(snapshot.Name, stored.Name);
    }

    [Fact]
    public async Task Create_when_RAC_fails_does_not_publish_or_retry()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.ClusterCreateException =
            new RasGateClientException("Port conflict details.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateClusterRequest("localhost", 1541),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "cluster_create_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("Port conflict", json.ToString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterCreateRequestCount);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterInfoRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Create_when_RAC_is_unavailable_returns_service_unavailable()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.ClusterCreateException =
            new RacUnavailableException(rasGate.Id);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateClusterRequest("localhost", 1541),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("rac_unavailable", GetErrorCode(json));
        Assert.Equal(1, _factory.RasGateBoundary.ClusterCreateRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Create_when_outcome_is_unknown_requires_synchronization()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.ClusterCreateException =
            new RasGateMutationOutcomeUnknownException(
                rasGate.Id,
                "clusters",
                "insert");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateClusterRequest("localhost", 1541),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "cluster_create_outcome_unknown",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("Refresh", json.ToString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterCreateRequestCount);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterInfoRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Theory]
    [InlineData("not-found")]
    [InlineData("protocol")]
    [InlineData("rac-unavailable")]
    [InlineData("timeout")]
    [InlineData("canceled")]
    public async Task Create_when_remote_read_back_fails_returns_not_confirmed(
        string failure)
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        _factory.RasGateBoundary.CreatedClusterId = clusterId;
        _factory.RasGateBoundary.ClusterException =
            CreateReadBackException(failure, rasGate.Id, clusterId);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateClusterRequest("localhost", 1587),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("cluster_create_not_confirmed", GetErrorCode(json));
        Assert.Contains("Refresh", json.ToString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterCreateRequestCount);
        Assert.Equal(1, _factory.RasGateBoundary.ClusterInfoRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Create_when_configuration_changes_after_remote_read_back_returns_not_confirmed_without_retry()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        _factory.RasGateBoundary.CreatedClusterId = clusterId;
        _factory.RasGateBoundary.Cluster = CreateSnapshot(
            clusterId,
            "Created cluster");
        _factory.RasGateBoundary.PauseClusterPublications();
        using var client = _factory.CreateAuthenticatedClient();
        using var configurationClient = _factory.CreateAuthenticatedClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        var mutationRequest = client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateClusterRequest("localhost", 1587),
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
        AssertMutationNotConfirmed(json, "cluster_create_not_confirmed");
        Assert.Equal(1, _factory.RasGateBoundary.ClusterCreateRequestCount);
        Assert.Equal(1, _factory.RasGateBoundary.ClusterInfoRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id, true));
    }

    [Fact]
    public async Task Create_with_invalid_port_rejects_request_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateClusterRequest("localhost", 0),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterCreateRequestCount);
    }

    [Fact]
    public async Task Create_with_undefined_load_balancing_mode_rejects_request_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new { host = "localhost", port = 1587, loadBalancingMode = 999 },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterCreateRequestCount);
    }

    [Fact]
    public async Task Create_without_insert_capability_fails_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.SupportsClusterInsert = false;
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateClusterRequest("localhost", 1587),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("rac_capability_not_supported", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateBoundary.ClusterCreateRequestCount);
    }
}
