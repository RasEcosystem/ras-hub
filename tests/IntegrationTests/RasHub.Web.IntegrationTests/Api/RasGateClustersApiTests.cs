using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Contracts.Common.Pagination;
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
    public async Task Synchronize_persists_and_returns_clusters()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var snapshot = CreateSnapshot(
            Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7"),
            "Локальный кластер");
        _factory.RasGateClientFactory.Clusters = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");
        var cluster = Assert.Single(
            data.GetProperty("items").EnumerateArray().ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(snapshot.ExternalId, cluster.GetProperty("id").GetGuid());
        Assert.Equal(snapshot.Name, cluster.GetProperty("name").GetString());
        Assert.Equal(snapshot.Host, cluster.GetProperty("host").GetString());
        Assert.Equal(snapshot.Port, cluster.GetProperty("port").GetInt32());
        Assert.Equal(
            "Performance",
            cluster.GetProperty("loadBalancingMode").GetString());
        Assert.True(cluster.GetProperty("killProblemProcesses").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            cluster.GetProperty("killByMemoryWithDump").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            cluster.GetProperty("pingPeriod").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            cluster.GetProperty("restartSchedule").ValueKind);
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterRequestCount);

        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal(snapshot.ExternalId, stored.ExternalId);
        Assert.Equal(snapshot.Name, stored.Name);
        Assert.NotEqual(default, stored.ObservedAt);
        var storedGate = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(storedGate?.LastSeenAt);
    }

    [Fact]
    public async Task Get_paged_returns_cached_clusters_without_synchronization()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.Clusters =
            [CreateSnapshot(Guid.NewGuid(), "Cached cluster")];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateClientFactory.Clusters = [];

        using var cachedResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/get-paged",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(cachedResponse);

        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.Single(json.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterRequestCount);
    }

    [Fact]
    public async Task Synchronize_by_id_updates_only_requested_cluster()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var first = CreateSnapshot(Guid.NewGuid(), "First cluster");
        var second = CreateSnapshot(Guid.NewGuid(), "Second cluster");
        _factory.RasGateClientFactory.Clusters = [first, second];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        var updated = CreateSnapshot(first.ExternalId, "First updated");
        _factory.RasGateClientFactory.Cluster = updated;

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
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterInfoRequestCount);

        var stored = await _factory.FindRasClustersAsync(rasGate.Id);
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, cluster =>
            cluster.ExternalId == first.ExternalId &&
            cluster.Name == "First updated");
        Assert.Contains(stored, cluster =>
            cluster.ExternalId == second.ExternalId &&
            cluster.Name == "Second cluster");
    }

    [Fact]
    public async Task Get_by_id_returns_cached_cluster_without_calling_gate()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cached cluster");
        _factory.RasGateClientFactory.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
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
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterInfoRequestCount);
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterRequestCount);
    }

    [Fact]
    public async Task Synchronize_by_id_without_info_capability_fails_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.SupportsClusterInfo = false;
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}/synchronize",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterInfoRequestCount);
    }

    [Fact]
    public async Task Synchronize_by_id_returns_cluster_sync_error_when_synchronization_fails()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        _factory.RasGateClientFactory.ClusterException =
            new RasGateClientException("Cluster unavailable.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{clusterId}/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "ras_gate_cluster_sync_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, _factory.RasGateClientFactory.ClusterInfoRequestCount);
    }

    [Fact]
    public async Task Update_remote_identity_invalidates_cached_status_and_clusters()
    {
        var rasGate = await _factory.SeedRasGateAsync(
            instanceName: "Old Gate",
            version: "1.0.0",
            statusObservedAt: DateTime.UtcNow.AddMinutes(-1));
        _factory.RasGateClientFactory.Clusters =
            [CreateSnapshot(Guid.NewGuid(), "Old cluster")];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}",
            new UpdateRasGateRequest(
                rasGate.Name,
                "https://replacement.example.test",
                9443,
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

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{Guid.NewGuid()}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "ras_gate_not_found",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterRequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cluster_endpoints_for_inactive_gate_return_conflict_without_calling_gate(
        bool synchronize)
    {
        var rasGate = await _factory.SeedRasGateAsync(isActive: false);
        using var client = _factory.CreateAuthenticatedClient();
        var path = $"/api/v1/ras-gates/{rasGate.Id}/clusters/" +
                   (synchronize ? "synchronize" : "get-paged");

        using var response = await client.PostAsJsonAsync(
            path,
            new PageRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "ras_gate_inactive",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterRequestCount);
    }

    [Fact]
    public async Task Synchronize_returns_bad_gateway_when_synchronization_fails()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.ClustersException =
            new RasGateClientException("Gate unavailable.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "ras_gate_clusters_sync_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, _factory.RasGateClientFactory.ClusterRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Synchronize_with_partial_snapshot_keeps_previous_complete_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var existing = CreateSnapshot(Guid.NewGuid(), "Existing cluster");
        _factory.RasGateClientFactory.Clusters = [existing];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        _factory.RasGateClientFactory.Clusters = [];
        _factory.RasGateClientFactory.ClusterSnapshotCompleteness =
            SnapshotCompleteness.Partial;

        using var partialResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
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
        _factory.RasGateClientFactory.SupportsClusterSnapshots = false;
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Create_publishes_and_returns_authoritative_cluster()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        var snapshot = CreateSnapshot(clusterId, "Новый кластер");
        _factory.RasGateClientFactory.CreatedClusterId = clusterId;
        _factory.RasGateClientFactory.Cluster = snapshot;
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateRasClusterRequest(
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
            response.Headers.Location?.OriginalString);
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterCreateRequestCount);
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterInfoRequestCount);
        var options = Assert.IsType<RasClusterCreationOptions>(
            _factory.RasGateClientFactory.LastClusterCreationOptions);
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
        _factory.RasGateClientFactory.ClusterCreateException =
            new RasGateClientException("Port conflict details.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateRasClusterRequest("localhost", 1541),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "ras_gate_cluster_create_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("Port conflict", json.ToString());
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterCreateRequestCount);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterInfoRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Create_when_outcome_is_unknown_requires_synchronization()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.ClusterCreateException =
            new RasGateMutationOutcomeUnknownException(
                rasGate.Id,
                "clusters",
                "insert");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateRasClusterRequest("localhost", 1541),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "ras_gate_cluster_create_outcome_unknown",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("Synchronize", json.ToString());
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterCreateRequestCount);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterInfoRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Create_when_info_fails_does_not_publish_or_retry()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.ClusterException =
            new RasGateClientException("Info failed.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateRasClusterRequest("localhost", 1587),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterCreateRequestCount);
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterInfoRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Create_with_invalid_port_rejects_request_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateRasClusterRequest("localhost", 0),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterCreateRequestCount);
    }

    [Fact]
    public async Task Create_with_undefined_load_balancing_mode_rejects_request_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new
            {
                host = "localhost",
                port = 1587,
                loadBalancingMode = 999
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterCreateRequestCount);
    }

    [Fact]
    public async Task Create_without_insert_capability_fails_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.SupportsClusterInsert = false;
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters",
            new CreateRasClusterRequest("localhost", 1587),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterCreateRequestCount);
    }

    [Fact]
    public async Task Update_publishes_and_returns_authoritative_cluster()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var initial = CreateSnapshot(Guid.NewGuid(), "Old cluster");
        _factory.RasGateClientFactory.Clusters = [initial];
        using var client = _factory.CreateAuthenticatedClient();
        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        var updated = CreateSnapshot(initial.ExternalId, "Updated cluster");
        _factory.RasGateClientFactory.Cluster = updated;
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{initial.ExternalId}",
            new UpdateRasClusterRequest(
                "Updated cluster",
                AgentUser: "agent-admin",
                AgentPassword: "agent-secret"),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Updated cluster", json.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterUpdateRequestCount);
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterInfoRequestCount);
        Assert.Equal(initial.ExternalId, _factory.RasGateClientFactory.UpdatedClusterId);
        var options = Assert.IsType<RasClusterUpdateOptions>(
            _factory.RasGateClientFactory.LastClusterUpdateOptions);
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
        _factory.RasGateClientFactory.Clusters = [initial];
        using var client = _factory.CreateAuthenticatedClient();
        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);
        _factory.RasGateClientFactory.ClusterUpdateException =
            new RasGateClientException("Update failed.");

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{initial.ExternalId}",
            new UpdateRasClusterRequest("Updated cluster"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterUpdateRequestCount);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterInfoRequestCount);
        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal("Old cluster", stored.Name);
    }

    [Fact]
    public async Task Update_when_outcome_is_unknown_keeps_shadow_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var initial = CreateSnapshot(Guid.NewGuid(), "Old cluster");
        _factory.RasGateClientFactory.Clusters = [initial];
        using var client = _factory.CreateAuthenticatedClient();
        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);
        _factory.RasGateClientFactory.ClusterUpdateException =
            new RasGateMutationOutcomeUnknownException(
                rasGate.Id,
                "clusters",
                "update");

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{initial.ExternalId}",
            new UpdateRasClusterRequest("Updated cluster"),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "ras_gate_cluster_update_outcome_unknown",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterUpdateRequestCount);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterInfoRequestCount);
        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal("Old cluster", stored.Name);
    }

    [Fact]
    public async Task Update_unknown_cluster_returns_not_found_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}",
            new UpdateRasClusterRequest("Updated cluster"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterUpdateRequestCount);
    }

    [Fact]
    public async Task Update_without_settings_rejects_request_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}",
            new UpdateRasClusterRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterUpdateRequestCount);
    }

    [Fact]
    public async Task Remove_deletes_remote_cluster_and_soft_deletes_shadow_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to remove");
        _factory.RasGateClientFactory.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}")
        {
            Content = JsonContent.Create(new RemoveRasClusterRequest(
                "cluster-admin",
                "cluster-secret"))
        };
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            cluster.ExternalId,
            json.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterRemoveRequestCount);
        Assert.Equal(
            cluster.ExternalId,
            _factory.RasGateClientFactory.RemovedClusterId);
        Assert.Equal(
            "cluster-admin",
            _factory.RasGateClientFactory.LastClusterUser);
        Assert.Equal(
            "cluster-secret",
            _factory.RasGateClientFactory.LastClusterPassword);
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

        using var response = await client.DeleteAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterRemoveRequestCount);
    }

    [Fact]
    public async Task Remove_with_password_but_no_user_rejects_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new RemoveRasClusterRequest(
                ClusterPassword: "cluster-secret"))
        };

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterRemoveRequestCount);
    }

    [Fact]
    public async Task Remove_when_RAC_fails_keeps_shadow_state_without_retry()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to keep");
        _factory.RasGateClientFactory.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateClientFactory.ClusterRemoveException =
            new RasGateClientException("Remove failed.");

        using var response = await client.DeleteAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "ras_gate_cluster_remove_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterRemoveRequestCount);
        Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Remove_when_outcome_is_unknown_keeps_shadow_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to keep");
        _factory.RasGateClientFactory.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateClientFactory.ClusterRemoveException =
            new RasGateMutationOutcomeUnknownException(
                rasGate.Id,
                "clusters",
                "remove");

        using var response = await client.DeleteAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "ras_gate_cluster_remove_outcome_unknown",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterRemoveRequestCount);
        Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Remove_without_capability_keeps_shadow_state_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = CreateSnapshot(Guid.NewGuid(), "Cluster to keep");
        _factory.RasGateClientFactory.Clusters = [cluster];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/synchronize",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateClientFactory.SupportsClusterRemove = false;

        using var response = await client.DeleteAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterRemoveRequestCount);
        Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Remove_from_inactive_gate_returns_conflict_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync(isActive: false);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterRemoveRequestCount);
    }

    [Fact]
    public async Task Get_paged_rejects_invalid_page_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/get-paged",
            new PageRequest(0),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
}
