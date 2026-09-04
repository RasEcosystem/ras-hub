using System.Net;
using System.Net.Http.Json;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Contracts.RasHub.Requests.Infobases;
using RasHub.Web.IntegrationTests.Infrastructure;
using static RasHub.Web.IntegrationTests.Api.ApiResponseTestHelpers;

namespace RasHub.Web.IntegrationTests.Api;

[Collection(WebApplicationCollection.Name)]
public sealed class RasGateInfobasesApiTests : IClassFixture<RasHubWebApplicationFactory>
{
    private readonly RasHubWebApplicationFactory _factory;

    public RasGateInfobasesApiTests(RasHubWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task Refresh_shadow_persists_infobases_and_returns_metadata()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var snapshot = CreateSnapshot(Guid.NewGuid(), "rim_demo", "Demo base");
        _factory.RasGateBoundary.Infobases = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            new InfobaseCredentialsRequest { ClusterUser = "cluster-admin", ClusterPassword = "cluster-secret" },
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.NotEqual(
            default,
            data.GetProperty("observedAt").GetDateTime());
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseRequestCount);
        Assert.Equal(
            cluster.ExternalId,
            _factory.RasGateBoundary.RequestedInfobaseClusterId);
        Assert.Equal(
            "cluster-admin",
            _factory.RasGateBoundary.LastClusterUser);
        Assert.Equal(
            "cluster-secret",
            _factory.RasGateBoundary.LastClusterPassword);

        var stored = Assert.Single(
            await _factory.FindRasInfobasesAsync(cluster.Id));
        Assert.Equal(snapshot.ExternalId, stored.ExternalId);
        Assert.Equal(snapshot.Name, stored.Name);
        Assert.NotEqual(default, stored.ObservedAt);
    }

    [Fact]
    public async Task Refresh_shadow_with_complete_empty_snapshot_removes_last_infobase()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        await _factory.SeedRasInfobaseAsync(cluster.Id);
        _factory.RasGateBoundary.Infobases = [];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            new InfobaseCredentialsRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await _factory.FindRasInfobasesAsync(cluster.Id));
        Assert.True(Assert.Single(
            await _factory.FindRasInfobasesAsync(cluster.Id, true)).IsDeleted);
    }

    [Fact]
    public async Task Get_live_all_refreshes_and_returns_complete_shadow()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var second = CreateSnapshot(Guid.NewGuid(), "second");
        var first = CreateSnapshot(Guid.NewGuid(), "first");
        _factory.RasGateBoundary.Infobases = [second, first];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/live/all",
            new InfobaseCredentialsRequest { ClusterUser = "cluster-admin", ClusterPassword = "cluster-secret" },
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
            infobase => Assert.NotEqual(
                default,
                infobase.GetProperty("observedAt").GetDateTime()));
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseRequestCount);
        Assert.Equal(
            "cluster-admin",
            _factory.RasGateBoundary.LastClusterUser);
        Assert.Equal(
            "cluster-secret",
            _factory.RasGateBoundary.LastClusterPassword);

        var stored = await _factory.FindRasInfobasesAsync(cluster.Id);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Get_shadow_paged_returns_persisted_infobases_without_live_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        _factory.RasGateBoundary.Infobases =
            [CreateSnapshot(Guid.NewGuid(), "cached")];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateBoundary.Infobases = [];
        using var cachedResponse = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow?page=1&pageSize=10",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(cachedResponse);

        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.Single(json.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Get_shadow_all_returns_complete_persisted_collection_without_live_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var snapshot = CreateSnapshot(Guid.NewGuid(), "shadow");
        _factory.RasGateBoundary.Infobases = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var refreshResponse = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        _factory.RasGateBoundary.Infobases = [];

        using var response = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/all",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(data);
        Assert.Equal(snapshot.ExternalId, data[0].GetProperty("id").GetGuid());
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Get_live_paged_refreshes_complete_shadow_and_returns_requested_page()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var first = CreateSnapshot(Guid.NewGuid(), "first");
        var second = CreateSnapshot(Guid.NewGuid(), "second");
        var third = CreateSnapshot(Guid.NewGuid(), "third");
        _factory.RasGateBoundary.Infobases = [third, first, second];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/live?page=2&pageSize=2",
            new InfobaseCredentialsRequest { ClusterUser = "cluster-admin", ClusterPassword = "cluster-secret" },
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
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseRequestCount);
        Assert.Equal("cluster-admin", _factory.RasGateBoundary.LastClusterUser);
        Assert.Equal("cluster-secret", _factory.RasGateBoundary.LastClusterPassword);
        Assert.Equal(
            3,
            (await _factory.FindRasInfobasesAsync(cluster.Id)).Count);
    }

    [Fact]
    public async Task Search_shadow_paged_searches_all_clusters_and_returns_parent_context()
    {
        var firstGate = await _factory.SeedRasGateAsync("First Gate");
        var secondGate = await _factory.SeedRasGateAsync("Second Gate");
        var firstCluster = await _factory.SeedRasClusterAsync(
            firstGate.Id,
            name: "First Cluster");
        var secondCluster = await _factory.SeedRasClusterAsync(
            secondGate.Id,
            name: "Second Cluster");
        var firstInfobase = await _factory.SeedRasInfobaseAsync(
            firstCluster.Id,
            name: "Alpha target");
        var secondInfobase = await _factory.SeedRasInfobaseAsync(
            secondCluster.Id,
            name: "Beta target");
        await _factory.SeedRasInfobaseAsync(
            secondCluster.Id,
            name: "Unrelated");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            "/api/v1/infobases/shadow/search?query=TARGET&page=1&pageSize=10",
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
                    item.GetProperty("clusterId").GetGuid());
                Assert.Equal("First Cluster", item.GetProperty("clusterName").GetString());
                Assert.Equal(
                    firstInfobase.ExternalId,
                    item.GetProperty("infobase").GetProperty("id").GetGuid());
            },
            item =>
            {
                Assert.Equal(secondGate.Id, item.GetProperty("rasGateId").GetGuid());
                Assert.Equal(
                    secondCluster.ExternalId,
                    item.GetProperty("clusterId").GetGuid());
                Assert.Equal(
                    secondInfobase.ExternalId,
                    item.GetProperty("infobase").GetProperty("id").GetGuid());
            });
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseRequestCount);
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseInfoRequestCount);
    }

    [Fact]
    public async Task Search_shadow_all_applies_parent_scoped_filters_and_description_field()
    {
        var expectedGate = await _factory.SeedRasGateAsync("Expected Gate");
        var otherGate = await _factory.SeedRasGateAsync("Other Gate");
        var sharedClusterId = Guid.NewGuid();
        var expectedCluster = await _factory.SeedRasClusterAsync(
            expectedGate.Id,
            sharedClusterId,
            "Expected Cluster");
        var otherCluster = await _factory.SeedRasClusterAsync(
            otherGate.Id,
            sharedClusterId,
            "Other Cluster");
        var expectedInfobase = await _factory.SeedRasInfobaseAsync(
            expectedCluster.Id,
            name: "Name does not match",
            description: "Target description");
        await _factory.SeedRasInfobaseAsync(
            otherCluster.Id,
            name: "Target appears only in name",
            description: "Target description");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"/api/v1/infobases/shadow/search/all?query=TARGET&rasGateId={expectedGate.Id}&clusterId={sharedClusterId}&fields=Description",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var item = Assert.Single(json.GetProperty("data").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedGate.Id, item.GetProperty("rasGateId").GetGuid());
        Assert.Equal(sharedClusterId, item.GetProperty("clusterId").GetGuid());
        Assert.Equal(
            expectedInfobase.ExternalId,
            item.GetProperty("infobase").GetProperty("id").GetGuid());
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Search_shadow_rejects_cluster_filter_without_gate_filter()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"/api/v1/infobases/shadow/search?query=base&clusterId={Guid.NewGuid()}&page=1&pageSize=10",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Get_live_one_updates_only_requested_infobase()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var first = CreateSnapshot(Guid.NewGuid(), "first");
        var second = CreateSnapshot(Guid.NewGuid(), "second");
        _factory.RasGateBoundary.Infobases = [first, second];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            new InfobaseCredentialsRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        var updated = CreateSnapshot(first.ExternalId, "first-updated");
        _factory.RasGateBoundary.Infobase = updated;
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/live/{first.ExternalId}",
            new InfobaseCredentialsRequest { ClusterUser = "cluster-admin", ClusterPassword = "cluster-secret" },
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            updated.ExternalId,
            json.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(
            updated.Name,
            json.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseInfoRequestCount);
        Assert.Equal(
            first.ExternalId,
            _factory.RasGateBoundary.RequestedInfobaseId);

        var stored = await _factory.FindRasInfobasesAsync(cluster.Id);
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored,
            infobase =>
                infobase.ExternalId == first.ExternalId &&
                infobase.Name == "first-updated");
        Assert.Contains(stored,
            infobase =>
                infobase.ExternalId == second.ExternalId &&
                infobase.Name == "second");
    }

    [Fact]
    public async Task Get_live_one_missing_remote_infobase_removes_stale_shadow_and_returns_not_found()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var infobase = await _factory.SeedRasInfobaseAsync(cluster.Id);
        var sibling = await _factory.SeedRasInfobaseAsync(cluster.Id);
        _factory.RasGateBoundary.InfobaseException =
            new RacResourceNotFoundException("infobases", infobase.ExternalId);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/live/{infobase.ExternalId}",
            new InfobaseCredentialsRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("infobase_not_found", GetErrorCode(json));
        Assert.Equal(
            $"Infobase '{infobase.ExternalId}' was not found.",
            json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseInfoRequestCount);
        var activeInfobases = await _factory.FindRasInfobasesAsync(cluster.Id);
        Assert.Equal(sibling.ExternalId, Assert.Single(activeInfobases).ExternalId);
        var allInfobases = await _factory.FindRasInfobasesAsync(cluster.Id, true);
        Assert.True(allInfobases.Single(item => item.Id == infobase.Id).IsDeleted);
    }

    [Fact]
    public async Task Get_shadow_one_returns_persisted_infobase_without_live_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var snapshot = CreateSnapshot(Guid.NewGuid(), "cached");
        _factory.RasGateBoundary.Infobases = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            new InfobaseCredentialsRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        using var response = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/{snapshot.ExternalId}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            snapshot.ExternalId,
            json.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseInfoRequestCount);
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Refresh_shadow_for_unknown_cluster_returns_not_found_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var clusterId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{clusterId}/infobases/shadow/refresh",
            new InfobaseCredentialsRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("cluster_not_found", GetErrorCode(json));
        Assert.Equal(
            $"Cluster '{clusterId}' was not found.",
            json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Refresh_shadow_when_task_loses_cluster_returns_not_found()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        _factory.RasGateBoundary.InfobasesException =
            new RasClusterNotFoundException(
                rasGate.Id,
                cluster.ExternalId);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("cluster_not_found", GetErrorCode(json));
        Assert.Equal(
            $"Cluster '{cluster.ExternalId}' was not found.",
            json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Refresh_shadow_returns_bad_gateway_when_live_refresh_fails()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        _factory.RasGateBoundary.InfobasesException =
            new RasGateClientException("Sensitive remote details.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            new InfobaseCredentialsRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "infobase_shadow_refresh_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("Sensitive remote details", json.ToString());
        Assert.Equal(2, _factory.RasGateBoundary.InfobaseRequestCount);
        Assert.Empty(await _factory.FindRasInfobasesAsync(cluster.Id));
    }

    [Fact]
    public async Task Get_live_all_returns_live_refresh_error_when_refresh_fails()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        _factory.RasGateBoundary.InfobasesException =
            new RasGateClientException("Sensitive remote details.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/live/all",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("infobase_live_refresh_failed", GetErrorCode(json));
        Assert.DoesNotContain("Sensitive remote details", json.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_request_when_RAC_is_unavailable_returns_service_unavailable(
        bool singleResource)
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var infobaseId = Guid.NewGuid();
        var exception = new RacUnavailableException(rasGate.Id);

        if (singleResource)
            _factory.RasGateBoundary.InfobaseException = exception;
        else
            _factory.RasGateBoundary.InfobasesException = exception;

        using var client = _factory.CreateAuthenticatedClient();
        var path = $"/api/v1/ras-gates/{rasGate.Id}/clusters/" +
                   $"{cluster.ExternalId}/infobases" +
                   (singleResource
                       ? $"/live/{infobaseId}"
                       : "/shadow/refresh");

        using var response = singleResource
            ? await client.PostAsJsonAsync(
                path,
                new InfobaseCredentialsRequest(),
                TestContext.Current.CancellationToken)
            : await client.PostAsJsonAsync(
                path,
                new InfobaseCredentialsRequest(),
                TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("rac_unavailable", GetErrorCode(json));
    }

    [Fact]
    public async Task Refresh_shadow_with_partial_snapshot_keeps_previous_complete_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var existing = CreateSnapshot(Guid.NewGuid(), "existing");
        _factory.RasGateBoundary.Infobases = [existing];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            new InfobaseCredentialsRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        _factory.RasGateBoundary.Infobases = [];
        _factory.RasGateBoundary.InfobaseSnapshotCompleteness =
            SnapshotCompleteness.Partial;
        using var partialResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            new InfobaseCredentialsRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, partialResponse.StatusCode);
        var stored = Assert.Single(
            await _factory.FindRasInfobasesAsync(cluster.Id));
        Assert.Equal(existing.ExternalId, stored.ExternalId);
        Assert.False(stored.IsDeleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_request_without_capability_returns_conflict_without_execution(
        bool singleResource)
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);

        if (singleResource)
            _factory.RasGateBoundary.SupportsInfobaseInfo = false;
        else
            _factory.RasGateBoundary.SupportsInfobaseSnapshots = false;

        using var client = _factory.CreateAuthenticatedClient();
        var path = $"/api/v1/ras-gates/{rasGate.Id}/clusters/" +
                   $"{cluster.ExternalId}/infobases" +
                   (singleResource
                       ? $"/live/{Guid.NewGuid()}"
                       : "/shadow/refresh");

        using var response = singleResource
            ? await client.PostAsJsonAsync(
                path,
                new InfobaseCredentialsRequest(),
                TestContext.Current.CancellationToken)
            : await client.PostAsJsonAsync(
                path,
                new InfobaseCredentialsRequest(),
                TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("rac_capability_not_supported", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseRequestCount);
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseInfoRequestCount);
    }

    [Fact]
    public async Task Refresh_shadow_with_password_but_no_user_rejects_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/refresh",
            new InfobaseCredentialsRequest { ClusterPassword = "cluster-secret" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Get_shadow_one_missing_infobase_returns_not_found()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var infobaseId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/shadow/{infobaseId}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("infobase_not_found", GetErrorCode(json));
        Assert.Equal(
            $"Infobase '{infobaseId}' was not found.",
            json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseInfoRequestCount);
    }

    private static RasInfobaseSnapshot CreateSnapshot(
        Guid externalId,
        string name,
        string description = "")
    {
        return new RasInfobaseSnapshot { ExternalId = externalId, Name = name, Description = description };
    }
}
