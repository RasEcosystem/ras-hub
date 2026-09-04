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
public sealed partial class RasGateClustersApiTests : IClassFixture<RasHubWebApplicationFactory>
{
    private readonly RasHubWebApplicationFactory _factory;

    public RasGateClustersApiTests(RasHubWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task Refresh_shadow_persists_clusters_and_returns_metadata()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var snapshot = CreateSnapshot(
            Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7"),
            "Локальный кластер");
        _factory.RasGateBoundary.Clusters = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
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
    public async Task Refresh_shadow_with_complete_empty_snapshot_removes_last_cluster_and_infobases()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        await _factory.SeedRasInfobaseAsync(cluster.Id);
        _factory.RasGateBoundary.Clusters = [];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.True(Assert.Single(
            await _factory.FindRasClustersAsync(rasGate.Id, true)).IsDeleted);
        Assert.Empty(await _factory.FindRasInfobasesAsync(cluster.Id));
        Assert.True(Assert.Single(
            await _factory.FindRasInfobasesAsync(cluster.Id, true)).IsDeleted);
    }

    [Fact]
    public async Task Get_live_all_refreshes_and_returns_complete_shadow()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var second = CreateSnapshot(Guid.NewGuid(), "Second cluster");
        var first = CreateSnapshot(Guid.NewGuid(), "First cluster");
        _factory.RasGateBoundary.Clusters = [second, first];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/live/all",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(2, data.Length);
        Assert.Equal(first.ExternalId, data[0].GetProperty("id").GetGuid());
        Assert.Equal(second.ExternalId, data[1].GetProperty("id").GetGuid());
        Assert.All(
            data,
            cluster => Assert.NotEqual(
                default,
                cluster.GetProperty("observedAt").GetDateTime()));
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRequestCount);

        var stored = await _factory.FindRasClustersAsync(rasGate.Id);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Get_shadow_paged_returns_persisted_clusters_without_live_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.Clusters =
            [CreateSnapshot(Guid.NewGuid(), "Cached cluster")];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateBoundary.Clusters = [];

        using var cachedResponse = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow?page=1&pageSize=10",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(cachedResponse);

        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.Single(json.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRequestCount);
    }

    [Fact]
    public async Task Get_shadow_all_returns_complete_persisted_collection_without_live_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var snapshot = CreateSnapshot(Guid.NewGuid(), "Shadow cluster");
        _factory.RasGateBoundary.Clusters = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var refreshResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        _factory.RasGateBoundary.Clusters = [];

        using var response = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/all",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(data);
        Assert.Equal(snapshot.ExternalId, data[0].GetProperty("id").GetGuid());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRequestCount);
    }

    [Fact]
    public async Task Get_live_paged_refreshes_complete_shadow_and_returns_requested_page()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var first = CreateSnapshot(Guid.NewGuid(), "First cluster");
        var second = CreateSnapshot(Guid.NewGuid(), "Second cluster");
        var third = CreateSnapshot(Guid.NewGuid(), "Third cluster");
        _factory.RasGateBoundary.Clusters = [third, first, second];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/live?page=2&pageSize=2",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("page").GetInt32());
        Assert.Equal(2, data.GetProperty("pageSize").GetInt32());
        var item = Assert.Single(items);
        Assert.Equal(third.ExternalId, item.GetProperty("id").GetGuid());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterRequestCount);
        Assert.Equal(
            3,
            (await _factory.FindRasClustersAsync(rasGate.Id)).Count);
    }

    [Fact]
    public async Task Search_shadow_paged_searches_all_gates_and_returns_parent_context()
    {
        var firstGate = await _factory.SeedRasGateAsync("First Gate");
        var secondGate = await _factory.SeedRasGateAsync("Second Gate");
        var firstCluster = await _factory.SeedRasClusterAsync(
            firstGate.Id,
            name: "Alpha target");
        var secondCluster = await _factory.SeedRasClusterAsync(
            secondGate.Id,
            name: "Beta target");
        await _factory.SeedRasClusterAsync(
            secondGate.Id,
            name: "Unrelated");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            "/api/v1/clusters/shadow/search?query=TARGET&page=1&pageSize=10",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal(firstGate.Id, item.GetProperty("rasGateId").GetGuid());
                Assert.Equal("First Gate", item.GetProperty("rasGateName").GetString());
                Assert.Equal(
                    firstCluster.ExternalId,
                    item.GetProperty("cluster").GetProperty("id").GetGuid());
            },
            item =>
            {
                Assert.Equal(secondGate.Id, item.GetProperty("rasGateId").GetGuid());
                Assert.Equal("Second Gate", item.GetProperty("rasGateName").GetString());
                Assert.Equal(
                    secondCluster.ExternalId,
                    item.GetProperty("cluster").GetProperty("id").GetGuid());
            });
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRequestCount);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterInfoRequestCount);
    }

    [Fact]
    public async Task Search_shadow_all_applies_optional_gate_and_selected_host_field()
    {
        var expectedGate = await _factory.SeedRasGateAsync("Expected Gate");
        var otherGate = await _factory.SeedRasGateAsync("Other Gate");
        var expectedCluster = await _factory.SeedRasClusterAsync(
            expectedGate.Id,
            name: "Name does not match",
            host: "target-host.example.test");
        await _factory.SeedRasClusterAsync(
            otherGate.Id,
            name: "Target appears only in name",
            host: "target-host.example.test");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"/api/v1/clusters/shadow/search/all?query=TARGET&rasGateId={expectedGate.Id}&fields=Host",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var item = Assert.Single(json.GetProperty("data").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedGate.Id, item.GetProperty("rasGateId").GetGuid());
        Assert.Equal(
            expectedCluster.ExternalId,
            item.GetProperty("cluster").GetProperty("id").GetGuid());
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRequestCount);
    }

    [Fact]
    public async Task Get_live_one_updates_only_requested_cluster()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var first = CreateSnapshot(Guid.NewGuid(), "First cluster");
        var second = CreateSnapshot(Guid.NewGuid(), "Second cluster");
        _factory.RasGateBoundary.Clusters = [first, second];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        var updated = CreateSnapshot(first.ExternalId, "First updated");
        _factory.RasGateBoundary.Cluster = updated;

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/live/{first.ExternalId}",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        using var response = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/{cluster.ExternalId}",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/live/{Guid.NewGuid()}",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/live/{clusterId}",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "cluster_live_refresh_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, _factory.RasGateBoundary.ClusterInfoRequestCount);
    }

    [Fact]
    public async Task Synchronize_by_id_missing_remote_cluster_removes_stale_shadow_and_returns_not_found()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        await _factory.SeedRasInfobaseAsync(cluster.Id);
        var sibling = await _factory.SeedRasClusterAsync(rasGate.Id);
        _factory.RasGateBoundary.ClusterException =
            new RacResourceNotFoundException("clusters", cluster.ExternalId);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/live/{cluster.ExternalId}",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("cluster_not_found", GetErrorCode(json));
        Assert.Equal(
            $"Cluster '{cluster.ExternalId}' was not found.",
            json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.ClusterInfoRequestCount);
        var activeClusters = await _factory.FindRasClustersAsync(rasGate.Id);
        Assert.Equal(sibling.ExternalId, Assert.Single(activeClusters).ExternalId);
        var allClusters = await _factory.FindRasClustersAsync(rasGate.Id, true);
        Assert.True(allClusters.Single(item => item.Id == cluster.Id).IsDeleted);
        Assert.Empty(await _factory.FindRasInfobasesAsync(cluster.Id));
        Assert.True(Assert.Single(
            await _factory.FindRasInfobasesAsync(cluster.Id, true)).IsDeleted);
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
                       ? $"/live/{clusterId}"
                       : "/shadow/refresh");

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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
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
                rasGate.ConfigurationRevision,
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
            $"/api/v1/ras-gates/{Guid.NewGuid()}/clusters/shadow/refresh",
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
                   (synchronize ? "/shadow/refresh" : "/shadow");

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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "cluster_shadow_refresh_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, _factory.RasGateBoundary.ClusterRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
    }

    [Fact]
    public async Task Get_live_all_returns_live_refresh_error_when_refresh_fails()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.ClustersException =
            new RasGateClientException("Gate unavailable.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/live/all",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("cluster_live_refresh_failed", GetErrorCode(json));
        Assert.DoesNotContain("Gate unavailable", json.ToString());
    }

    [Fact]
    public async Task Synchronize_with_partial_snapshot_keeps_previous_complete_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var existing = CreateSnapshot(Guid.NewGuid(), "Existing cluster");
        _factory.RasGateBoundary.Clusters = [existing];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        _factory.RasGateBoundary.Clusters = [];
        _factory.RasGateBoundary.ClusterSnapshotCompleteness =
            SnapshotCompleteness.Partial;

        using var partialResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
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
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("rac_capability_not_supported", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRequestCount);
        Assert.Empty(await _factory.FindRasClustersAsync(rasGate.Id));
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
                1,
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
        Assert.Contains("Refresh the cluster shadow", message);
        Assert.Contains("verify the target RasGate", message);
        Assert.Contains("Do not retry the mutation automatically", message);
    }
}
