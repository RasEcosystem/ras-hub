using System.Net;
using System.Net.Http.Json;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Requests;
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
    public async Task Synchronize_persists_and_returns_infobases()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var snapshot = CreateSnapshot(Guid.NewGuid(), "rim_demo", "Demo base");
        _factory.RasGateBoundary.Infobases = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/synchronize",
            new SynchronizeInfobasesRequest
            {
                ClusterUser = "cluster-admin",
                ClusterPassword = "cluster-secret"
            },
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");
        var infobase = Assert.Single(
            data.GetProperty("items").EnumerateArray().ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(snapshot.ExternalId, infobase.GetProperty("id").GetGuid());
        Assert.Equal(snapshot.Name, infobase.GetProperty("name").GetString());
        Assert.Equal(
            snapshot.Description,
            infobase.GetProperty("description").GetString());
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
    public async Task Get_paged_returns_cached_infobases_without_synchronization()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        _factory.RasGateBoundary.Infobases =
            [CreateSnapshot(Guid.NewGuid(), "cached")];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/synchronize",
            new SynchronizeInfobasesRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        _factory.RasGateBoundary.Infobases = [];
        using var cachedResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/get-paged",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(cachedResponse);

        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.Single(json.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(1, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Synchronize_by_id_updates_only_requested_infobase()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var first = CreateSnapshot(Guid.NewGuid(), "first");
        var second = CreateSnapshot(Guid.NewGuid(), "second");
        _factory.RasGateBoundary.Infobases = [first, second];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/synchronize",
            new SynchronizeInfobasesRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        var updated = CreateSnapshot(first.ExternalId, "first-updated");
        _factory.RasGateBoundary.Infobase = updated;
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/{first.ExternalId}/synchronize",
            new SynchronizeInfobaseRequest
            {
                ClusterUser = "cluster-admin",
                ClusterPassword = "cluster-secret"
            },
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
        Assert.Contains(stored, infobase =>
            infobase.ExternalId == first.ExternalId &&
            infobase.Name == "first-updated");
        Assert.Contains(stored, infobase =>
            infobase.ExternalId == second.ExternalId &&
            infobase.Name == "second");
    }

    [Fact]
    public async Task Get_by_id_returns_cached_infobase_without_calling_gate()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var snapshot = CreateSnapshot(Guid.NewGuid(), "cached");
        _factory.RasGateBoundary.Infobases = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var synchronizationResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/synchronize",
            new SynchronizeInfobasesRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, synchronizationResponse.StatusCode);

        using var response = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/{snapshot.ExternalId}",
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
    public async Task Synchronize_for_unknown_cluster_returns_not_found_without_execution()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{Guid.NewGuid()}/infobases/synchronize",
            new SynchronizeInfobasesRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "ras_cluster_not_found",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Synchronize_returns_bad_gateway_when_synchronization_fails()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        _factory.RasGateBoundary.InfobasesException =
            new RasGateClientException("Sensitive remote details.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/synchronize",
            new SynchronizeInfobasesRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "ras_gate_infobases_sync_failed",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("Sensitive remote details", json.ToString());
        Assert.Equal(2, _factory.RasGateBoundary.InfobaseRequestCount);
        Assert.Empty(await _factory.FindRasInfobasesAsync(cluster.Id));
    }

    [Fact]
    public async Task Synchronize_with_partial_snapshot_keeps_previous_complete_state()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        var existing = CreateSnapshot(Guid.NewGuid(), "existing");
        _factory.RasGateBoundary.Infobases = [existing];
        using var client = _factory.CreateAuthenticatedClient();

        using var initialResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/synchronize",
            new SynchronizeInfobasesRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        _factory.RasGateBoundary.Infobases = [];
        _factory.RasGateBoundary.InfobaseSnapshotCompleteness =
            SnapshotCompleteness.Partial;
        using var partialResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/synchronize",
            new SynchronizeInfobasesRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, partialResponse.StatusCode);
        var stored = Assert.Single(
            await _factory.FindRasInfobasesAsync(cluster.Id));
        Assert.Equal(existing.ExternalId, stored.ExternalId);
        Assert.False(stored.IsDeleted);
    }

    [Fact]
    public async Task Synchronize_with_password_but_no_user_rejects_request()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/synchronize",
            new SynchronizeInfobasesRequest
            {
                ClusterPassword = "cluster-secret"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    [Fact]
    public async Task Get_missing_infobase_returns_not_found()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var cluster = await _factory.SeedRasClusterAsync(rasGate.Id);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/{cluster.ExternalId}/infobases/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "ras_infobase_not_found",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseInfoRequestCount);
    }

    [Fact]
    public async Task Synchronize_without_authentication_returns_unauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{Guid.NewGuid()}/clusters/{Guid.NewGuid()}/infobases/synchronize",
            new SynchronizeInfobasesRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _factory.RasGateBoundary.InfobaseRequestCount);
    }

    private static RasInfobaseSnapshot CreateSnapshot(
        Guid externalId,
        string name,
        string description = "")
    {
        return new RasInfobaseSnapshot
        {
            ExternalId = externalId,
            Name = name,
            Description = description
        };
    }
}