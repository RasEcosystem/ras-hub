using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.Contracts.RasHub.Requests;
using static RasHub.Web.IntegrationTests.Api.ApiResponseTestHelpers;

namespace RasHub.Web.IntegrationTests.Api;

public sealed partial class RasGatesApiTests
{
    [Fact]
    public async Task Get_status_returns_cached_status()
    {
        var observedAt = DateTime.UtcNow.AddMinutes(-5);
        var rasGate = await _factory.SeedRasGateAsync(
            instanceName: "Cached Gate",
            version: "1.2.3",
            statusObservedAt: observedAt);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}/status",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cached Gate", data.GetProperty("instanceName").GetString());
        Assert.Equal("1.2.3", data.GetProperty("version").GetString());
        Assert.Equal(observedAt, data.GetProperty("observedAt").GetDateTime());
        Assert.Equal(0, _factory.RasGateClientFactory.StatusRequestCount);
    }

    [Fact]
    public async Task Synchronize_status_calls_gate_persists_and_returns_status()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.Status =
            new RasGateStatus(
                "Remote Gate",
                "2.3.4");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Remote Gate", data.GetProperty("instanceName").GetString());
        Assert.Equal("2.3.4", data.GetProperty("version").GetString());
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("observedAt").ValueKind);
        Assert.Equal(1, _factory.RasGateClientFactory.StatusRequestCount);
        Assert.Equal("stored-secret", _factory.RasGateClientFactory.LastApiKey);

        var stored = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(stored);
        Assert.Equal("Remote Gate", stored.InstanceName);
        Assert.Equal("2.3.4", stored.Version);
        Assert.NotNull(stored.StatusObservedAt);
        Assert.NotNull(stored.LastSeenAt);
        Assert.Equal(stored.StatusObservedAt, stored.LastSeenAt);

        using var cachedResponse = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}/status",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.Equal(1, _factory.RasGateClientFactory.StatusRequestCount);
    }

    [Fact]
    public async Task Status_response_from_previous_configuration_is_not_published()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.Status =
            new RasGateStatus("Old remote Gate", "1.0.0");
        _factory.RasGateClientFactory.PauseStatusRequests();
        using var client = _factory.CreateAuthenticatedClient();
        var synchronizationTask = client.PostAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/synchronize",
            null,
            TestContext.Current.CancellationToken);

        try
        {
            await _factory.RasGateClientFactory.WaitForStatusRequestAsync(
                TestContext.Current.CancellationToken);

            using var updateResponse = await client.PutAsJsonAsync(
                $"{RasGatesPath}/{rasGate.Id}",
                new UpdateRasGateRequest(
                    rasGate.Name,
                    "https://replacement.example.test",
                    9443,
                    "replacement-secret"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        }
        finally
        {
            _factory.RasGateClientFactory.ReleaseStatusRequests();
        }

        using var synchronizationResponse = await synchronizationTask;
        var json = await ReadJsonAsync(synchronizationResponse);

        Assert.Equal(HttpStatusCode.Conflict, synchronizationResponse.StatusCode);
        Assert.Equal("ras_gate_configuration_changed", GetErrorCode(json));

        var stored = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(stored);
        Assert.Equal(2, stored.ConfigurationRevision);
        Assert.Null(stored.InstanceName);
        Assert.Null(stored.Version);
        Assert.Null(stored.StatusObservedAt);
        Assert.Null(stored.LastSeenAt);
    }

    [Fact]
    public async Task Synchronize_status_for_unknown_gate_returns_not_found_without_scheduling()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"{RasGatesPath}/{Guid.NewGuid()}/status/synchronize",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ras_gate_not_found", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateClientFactory.StatusRequestCount);
    }

    [Fact]
    public async Task Legacy_status_check_route_is_not_available()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/check",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(0, _factory.RasGateClientFactory.StatusRequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Status_endpoints_for_inactive_gate_return_conflict_without_calling_gate(
        bool synchronize)
    {
        var rasGate = await _factory.SeedRasGateAsync(isActive: false);
        using var client = _factory.CreateAuthenticatedClient();
        var path = $"{RasGatesPath}/{rasGate.Id}/status" +
                   (synchronize ? "/synchronize" : string.Empty);
        using var request = new HttpRequestMessage(
            synchronize ? HttpMethod.Post : HttpMethod.Get,
            path);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ras_gate_inactive", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateClientFactory.StatusRequestCount);
    }

    [Fact]
    public async Task Background_handlers_reject_inactive_gate_before_creating_requests()
    {
        var rasGate = await _factory.SeedRasGateAsync(isActive: false);
        using var scope = _factory.Services.CreateScope();
        var statusHandler = scope.ServiceProvider.GetRequiredService<
            IBackgroundTaskHandler<CheckRasGateStatusTask>>();
        var clustersHandler = scope.ServiceProvider.GetRequiredService<
            IBackgroundTaskHandler<SynchronizeClustersTask>>();

        await Assert.ThrowsAsync<RasGateInactiveException>(() =>
            statusHandler.ExecuteAsync(
                new CheckRasGateStatusTask(rasGate.Id),
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<RasGateInactiveException>(() =>
            clustersHandler.ExecuteAsync(
                new SynchronizeClustersTask(rasGate.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, _factory.RasGateClientFactory.StatusRequestCount);
        Assert.Equal(0, _factory.RasGateClientFactory.ClusterRequestCount);
    }
}
