using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Contracts.Common.Pagination;
using RasHub.Domain;
using RasHub.Domain.Enums;
using RasHub.Web.IntegrationTests.Infrastructure;

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
    public async Task Get_paged_with_refresh_synchronizes_persists_and_returns_clusters()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        var snapshot = CreateSnapshot(
            Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7"),
            "Локальный кластер");
        _factory.RasGateClientFactory.Clusters = [snapshot];
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/get-paged?refresh=true",
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
        Assert.Equal(1, _factory.RasGateClientFactory.ClusterRequestCount);

        var stored = Assert.Single(await _factory.FindRasClustersAsync(rasGate.Id));
        Assert.Equal(snapshot.ExternalId, stored.ExternalId);
        Assert.Equal(snapshot.Name, stored.Name);
        Assert.NotEqual(default, stored.ObservedAt);
    }

    [Fact]
    public async Task Get_paged_without_refresh_returns_cached_clusters()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.Clusters =
            [CreateSnapshot(Guid.NewGuid(), "Cached cluster")];
        using var client = _factory.CreateAuthenticatedClient();

        using var refreshResponse = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/get-paged?refresh=true",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

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
    public async Task Get_paged_for_unknown_gate_returns_not_found_without_synchronization()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{Guid.NewGuid()}/clusters/get-paged?refresh=true",
            new PageRequest(),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "ras_gate_not_found",
            json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterRequestCount);
    }

    [Fact]
    public async Task Get_paged_returns_bad_gateway_when_synchronization_fails()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.ClustersException =
            new RasGateClientException("Gate unavailable.");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/ras-gates/{rasGate.Id}/clusters/get-paged?refresh=true",
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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
        return document.RootElement.Clone();
    }
}