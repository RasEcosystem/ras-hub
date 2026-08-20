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

[Collection(WebApplicationCollection.Name)]
public sealed class RasGateClustersApiTests : IClassFixture<RasHubWebApplicationFactory>
{
    private readonly RasHubWebApplicationFactory _factory;

    public RasGateClustersApiTests(RasHubWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task Synchronize_persists_clusters_and_returns_metadata()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var snapshot = CreateSnapshot(
            Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7"),
            "Локальный кластер");
        _factory.RasGateBoundary.Clusters = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.NotEqual(
            default,
            data.GetProperty("observedAt").GetDateTime());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRequestCount);

        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal(snapshot.ExternalId, stored.ExternalId);
        Assert.Equal(snapshot.Name, stored.Name);
        Assert.NotEqual(default, stored.ObservedAt);
        var storedGate = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(storedGate?.LastSeenAt);
    }

    [Fact]
    public async Task List_returns_cached_clusters_without_synchronization()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.Clusters =
            [CreateSnapshot(Guid.NewGuid(), "Cached cluster")];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateBoundary.Clusters = [];

        using var cachedResponse = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters?page=1&pageSize=10",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(cachedResponse);

        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.Single(json.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRequestCount);
    }

    [Fact]
    public async Task Synchronize_by_id_updates_only_requested_cluster()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var first = CreateSnapshot(Guid.NewGuid(), "First cluster");
        var second = CreateSnapshot(Guid.NewGuid(), "Second cluster");
        _factory.RasGateBoundary.Clusters = [first, second];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        var updated = CreateSnapshot(first.ExternalId, "First updated");
        _factory.RasGateBoundary.Cluster = updated;

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{first.ExternalId}/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            updated.ExternalId,
            json.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(
            updated.Name,
            json.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterInfoRequestCount);

        var stored = await _factory.FindRasClustersAsync(rasGate.Id);
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored,
            cluster =>
                cluster.ExternalId == first.ExternalId &&
                cluster.Name == "First updated");
        Assert.Contains(stored,
            cluster =>
                cluster.ExternalId == second.ExternalId &&
                cluster.Name == "Second cluster");
    }

    [Fact]
    public async Task Get_by_id_returns_cached_cluster_without_calling_gate()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cached cluster");
        _factory.RasGateBoundary.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        using var response = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            cluster.ExternalId,
            json.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(0, _factory.RasGateBoundary.ClusterInfoRequestCount);
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRequestCount);
    }

    [Fact]
    public async Task Synchronize_by_id_without_info_capability_fails_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.SupportsClusterInfo = false;
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "rac_capability_not_supported",
            GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateBoundary.ClusterInfoRequestCount);
    }

    [Fact]
    public async Task Synchronize_by_id_returns_cluster_sync_error_when_synchronization_fails()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        _factory.RasGateBoundary.ClusterException =
            new RasGateClientException("Cluster unavailable.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{clusterId}/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "cluster_synchronization_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, _factory.RasGateBoundary.ClusterInfoRequestCount);
    }

    [Fact]
    public async Task Synchronize_by_id_missing_remote_cluster_returns_not_found()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        _factory.RasGateBoundary.ClusterException =
            new RacResourceNotFoundException("clusters", clusterId);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{clusterId}/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("cluster_not_found", GetErrorCode(json));
        Assert.Equal(
            $"Cluster '{clusterId}' was not found.",
            json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterInfoRequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Synchronize_when_RAC_is_unavailable_returns_service_unavailable(
        bool singleResource)
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        var exception = new RacUnavailableException(rasGate.Id);

        if (singleResource)
            _factory.RasGateBoundary.ClusterException = exception;
        else
            _factory.RasGateBoundary.ClustersException = exception;

        using var client = _factory.CreateAuthenticatedClient();
        var path = $"/api/v1/ras-gates/{rasGate.Id}/clusters" +
                   (singleResource
                       ? $"/{clusterId}/synchronize"
                       : "/synchronize");

        using var response = await client.PostAsync(
            path,
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("rac_unavailable", GetErrorCode(json));
    }

    [Fact]
    public async Task Update_remote_identity_invalidates_cached_status_and_clusters()
    {
        var rasGate = await _factory.SeedRasGateAsync(
            instanceName: "Old Gate",
            version: "1.0.0",
            statusObservedAt: DateTime.UtcNow.AddMinutes(-1));
        _factory.RasGateBoundary.Clusters =
            [CreateSnapshot(Guid.NewGuid(), "Old cluster")];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}",
            new UpdateRasGateRequest(
                rasGate.Name,
                "https://replacement.example.test",
                9443,
                rasGate.IsActive,
                "replacement-secret"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var storedGate = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(storedGate);
        Assert.Equal(2, storedGate.ConfigurationRevision);
        Assert.Null(storedGate.InstanceName);
        Assert.Null(storedGate.Version);
        Assert.Null(storedGate.StatusObservedAt);
        Assert.Null(storedGate.LastSeenAt);

        var storedClusters = await _factory.FindRasClustersAsync(
            rasGate.Id,
            true);
        var storedCluster = Assert.Single(storedClusters);
        Assert.True(storedCluster.IsDeleted);
    }

    [Fact]
    public async Task Synchronize_for_unknown_gate_returns_not_found_without_execution()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{Guid.NewGuid()}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "ras_gate_not_found",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cluster_endpoints_for_inactive_gate_return_conflict_without_calling_gate(
        bool synchronize)
    {
        var rasGate = await _factory.SeedRasGateAsync(isActive: false);
        using var client = _factory.CreateAuthenticatedClient();
        var path = $"/api/v1/ras-gates/{rasGate.Id}/clusters" +
                   (synchronize ? "/synchronize" : string.Empty);

        using var response = synchronize
            ? await client.PostAsync(
                path,
                null,
                TestContext.Current.CancellationToken)
            : await client.GetAsync(
                path,
                TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "ras_gate_inactive",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRequestCount);
    }

    [Fact]
    public async Task Synchronize_returns_bad_gateway_when_synchronization_fails()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.ClustersException =
            new RasGateClientException("Gate unavailable.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "clusters_synchronization_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, _factory.RasGateBoundary.ClusterRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Synchronize_with_partial_snapshot_keeps_previous_complete_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var existing = CreateSnapshot(Guid.NewGuid(), "Existing cluster");
        _factory.RasGateBoundary.Clusters = [existing];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        _factory.RasGateBoundary.Clusters = [];
        _factory.RasGateBoundary.ClusterSnapshotCompleteness =
            SnapshotCompleteness.Partial;

        using var partialResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, partialResponse.StatusCode);
        var stored = Assert.Single(
            await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal(existing.ExternalId, stored.ExternalId);
        Assert.False(stored.IsDeleted);
    }

    [Fact]
    public async Task Synchronize_without_cluster_capability_fails_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.SupportsClusterSnapshots = false;
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("rac_capability_not_supported", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

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
            $"/api/v1/ras-gates/{rasGate.Id:D}/clusters/{clusterId:D}",
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
        Assert.Contains("Synchronize", json.ToString());
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
        Assert.Contains("Synchronize", json.ToString());
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

    [Fact]
    public async Task Update_publishes_and_returns_authoritative_cluster()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var initial = CreateSnapshot(Guid.NewGuid(), "Old cluster");
        _factory.RasGateBoundary.Clusters = [initial];
        using var client = _factory.CreateAuthenticatedClient();
        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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
        Assert.Contains("Synchronize", json.ToString());
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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

    [Fact]
    public async Task Remove_deletes_remote_cluster_and_soft_deletes_shadow_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to remove");
        _factory.RasGateBoundary.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
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

    private static RasClusterSnapshot CreateSnapshot(Guid externalId, string name)
    {
        return new RasClusterSnapshot
        {
            ExternalId = externalId,
            Name = name,
            Host = "WIN-P4BDRRBVMU8",
            Port = 1541,
            ExpirationTimeoutSeconds = 60,
            LifetimeLimitSeconds = 0,
            MaxMemorySizeKb = 0,
            MaxMemoryTimeLimitSeconds = 0,
            SecurityLevel = 0,
            SessionFaultToleranceLevel = 0,
            LoadBalancingMode = RasClusterLoadBalancingMode.Performance,
            ErrorsCountThresholdPercent = 0,
            KillProblemProcesses = true
        };
    }

    private static Exception CreateReadBackException(
        string failure,
        Guid rasGateId,
        Guid clusterId)
    {
        return failure switch
        {
            "not-found" => new RacResourceNotFoundException(
                "clusters",
                clusterId),
            "protocol" => new RasGateClientException(
                "RasGate returned an invalid response."),
            "rac-unavailable" => new RacUnavailableException(rasGateId),
            "timeout" => new TimeoutException("RasGate request timed out."),
            "canceled" => new OperationCanceledException(
                "RasGate read-back was canceled."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                null)
        };
    }

    private static async Task ReconfigureRasGateAsync(
        HttpClient client,
        Guid rasGateId,
        string name,
        CancellationToken cancellationToken)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/ras-gates/{rasGateId}",
            new UpdateRasGateRequest(
                name,
                "https://replacement.example.test",
                9443,
                true,
                "replacement-secret"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void AssertMutationNotConfirmed(
        JsonElement json,
        string expectedCode)
    {
        Assert.Equal(expectedCode, GetErrorCode(json));
        var message = Assert.IsType<string>(json
            .GetProperty("error")
            .GetProperty("message")
            .GetString());
        Assert.Contains("Synchronize cluster state", message);
        Assert.Contains("verify the target RasGate", message);
        Assert.Contains("Do not retry the mutation automatically", message);
    }
}