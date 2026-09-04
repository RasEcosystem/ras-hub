using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Application.RasGates.Tasks.Clusters;
using RasHub.Application.RasGates.Tasks.Status;
using RasHub.BackgroundTasks.Abstractions;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Infrastructure.Database;
using static RasHub.Web.IntegrationTests.Api.ApiResponseTestHelpers;

namespace RasHub.Web.IntegrationTests.Api;

public sealed partial class RasGatesApiTests
{
    [Fact]
    public async Task Get_shadow_status_without_observation_returns_unknown()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/shadow",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Unknown", data.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("rasGateObservedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("racAvailable").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("racObservedAt").ValueKind);
        Assert.Equal(0, _factory.RasGateBoundary.StatusRequestCount);
    }

    [Fact]
    public async Task Get_shadow_status_with_stale_gate_observation_returns_offline()
    {
        var staleObservedAt = DateTime.UtcNow.AddHours(-1);
        var recentlySeenAt = DateTime.UtcNow;
        var rasGate = await _factory.SeedRasGateAsync(
            instanceName: "Stale Gate",
            version: "1.2.3",
            statusObservedAt: staleObservedAt);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
            var stored = await db.RasGates.SingleAsync(
                item => item.Id == rasGate.Id,
                TestContext.Current.CancellationToken);
            stored.RacAvailable = true;
            stored.RacVersion = "8.3.27.2214";
            stored.RacStatusObservedAt = recentlySeenAt;
            stored.LastSeenAt = recentlySeenAt;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/shadow",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Offline",
            json.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal(0, _factory.RasGateBoundary.StatusRequestCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Get_shadow_status_with_fresh_gate_and_unavailable_or_stale_RAC_returns_degraded(
        bool racAvailable,
        bool racObservationIsStale)
    {
        var observedAt = DateTime.UtcNow;
        var rasGate = await _factory.SeedRasGateAsync(
            instanceName: "Fresh Gate",
            version: "1.2.3",
            statusObservedAt: observedAt);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
            var stored = await db.RasGates.SingleAsync(
                item => item.Id == rasGate.Id,
                TestContext.Current.CancellationToken);
            stored.RacAvailable = racAvailable;
            stored.RacVersion = racAvailable ? "8.3.27.2214" : null;
            stored.RacStatusObservedAt = racObservationIsStale
                ? observedAt.AddHours(-1)
                : observedAt;
            stored.LastSeenAt = observedAt;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/shadow",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", data.GetProperty("state").GetString());
        Assert.Equal(
            racAvailable,
            data.GetProperty("racAvailable").GetBoolean());
        Assert.Equal(0, _factory.RasGateBoundary.StatusRequestCount);
    }

    [Fact]
    public async Task Get_shadow_status_returns_persisted_status_without_live_request()
    {
        var observedAt = DateTime.UtcNow;
        var rasGate = await _factory.SeedRasGateAsync(
            instanceName: "Cached Gate",
            version: "1.2.3",
            statusObservedAt: observedAt);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RasHubDbContext>();
            var stored = await db.RasGates.SingleAsync(
                item => item.Id == rasGate.Id,
                TestContext.Current.CancellationToken);
            stored.RacAvailable = true;
            stored.RacVersion = "8.3.27.2214";
            stored.RacStatusObservedAt = observedAt;
            stored.LastSeenAt = observedAt;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/shadow",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ready", data.GetProperty("state").GetString());
        Assert.Equal("Cached Gate", data.GetProperty("instanceName").GetString());
        Assert.Equal("1.2.3", data.GetProperty("rasGateVersion").GetString());
        Assert.Equal(
            observedAt,
            data.GetProperty("rasGateObservedAt").GetDateTime());
        Assert.True(data.GetProperty("racAvailable").GetBoolean());
        Assert.Equal("8.3.27.2214", data.GetProperty("racVersion").GetString());
        Assert.Equal(
            observedAt,
            data.GetProperty("racObservedAt").GetDateTime());
        Assert.Equal(0, _factory.RasGateBoundary.StatusRequestCount);
    }

    [Fact]
    public async Task Get_live_status_calls_gate_persists_and_returns_updated_shadow()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.Status =
            new RasGateStatus(
                "Remote Gate",
                "2.3.4",
                true,
                "8.3.27.2214");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/live",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ready", data.GetProperty("state").GetString());
        Assert.Equal("Remote Gate", data.GetProperty("instanceName").GetString());
        Assert.Equal("2.3.4", data.GetProperty("rasGateVersion").GetString());
        Assert.NotEqual(
            JsonValueKind.Null,
            data.GetProperty("rasGateObservedAt").ValueKind);
        Assert.True(data.GetProperty("racAvailable").GetBoolean());
        Assert.Equal("8.3.27.2214", data.GetProperty("racVersion").GetString());
        Assert.NotEqual(
            JsonValueKind.Null,
            data.GetProperty("racObservedAt").ValueKind);
        Assert.Equal(1, _factory.RasGateBoundary.StatusRequestCount);
        Assert.Equal("stored-secret", _factory.RasGateBoundary.LastApiKey);

        var stored = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(stored);
        Assert.Equal("Remote Gate", stored.InstanceName);
        Assert.Equal("2.3.4", stored.Version);
        Assert.NotNull(stored.StatusObservedAt);
        Assert.True(stored.RacAvailable);
        Assert.Equal("8.3.27.2214", stored.RacVersion);
        Assert.NotNull(stored.RacStatusObservedAt);
        Assert.NotNull(stored.LastSeenAt);
        Assert.Equal(stored.StatusObservedAt, stored.LastSeenAt);
        Assert.Equal(stored.StatusObservedAt, stored.RacStatusObservedAt);

        using var shadowResponse = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/shadow",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, shadowResponse.StatusCode);
        Assert.Equal(1, _factory.RasGateBoundary.StatusRequestCount);
    }

    [Fact]
    public async Task Status_response_from_previous_configuration_is_not_published()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.Status =
            new RasGateStatus("Old remote Gate", "1.0.0");
        _factory.RasGateBoundary.PauseStatusRequests();
        using var client = _factory.CreateAuthenticatedClient();
        var liveRequestTask = client.PostAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/live",
            null,
            TestContext.Current.CancellationToken);

        try
        {
            await _factory.RasGateBoundary.WaitForStatusRequestAsync(
                TestContext.Current.CancellationToken);

            using var updateResponse = await client.PutAsJsonAsync(
                $"{RasGatesPath}/{rasGate.Id}",
                new UpdateRasGateRequest(
                    rasGate.Name,
                    "https://replacement.example.test",
                    9443,
                    rasGate.IsActive,
                    rasGate.ConfigurationRevision,
                    "replacement-secret"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        }
        finally
        {
            _factory.RasGateBoundary.ReleaseStatusRequests();
        }

        using var liveResponse = await liveRequestTask;
        var json = await ReadJsonAsync(liveResponse);

        Assert.Equal(HttpStatusCode.Conflict, liveResponse.StatusCode);
        Assert.Equal("ras_gate_configuration_changed", GetErrorCode(json));

        var stored = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(stored);
        Assert.Equal(2, stored.ConfigurationRevision);
        Assert.Null(stored.InstanceName);
        Assert.Null(stored.Version);
        Assert.Null(stored.StatusObservedAt);
        Assert.Null(stored.RacAvailable);
        Assert.Null(stored.RacVersion);
        Assert.Null(stored.RacStatusObservedAt);
        Assert.Null(stored.LastSeenAt);
    }

    [Fact]
    public async Task Get_live_status_for_unknown_gate_returns_not_found_without_scheduling()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"{RasGatesPath}/{Guid.NewGuid()}/status/live",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ras_gate_not_found", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateBoundary.StatusRequestCount);
    }

    [Fact]
    public async Task Get_live_status_when_task_loses_gate_returns_not_found()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateBoundary.StatusException =
            new RasGateNotFoundException(rasGate.Id);
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            $"{RasGatesPath}/{rasGate.Id}/status/live",
            null,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ras_gate_not_found", GetErrorCode(json));
        Assert.Equal(
            $"RasGate '{rasGate.Id}' was not found.",
            json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(1, _factory.RasGateBoundary.StatusRequestCount);
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
        Assert.Equal(0, _factory.RasGateBoundary.StatusRequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Status_endpoints_for_inactive_gate_return_conflict_without_calling_gate(
        bool live)
    {
        var rasGate = await _factory.SeedRasGateAsync(isActive: false);
        using var client = _factory.CreateAuthenticatedClient();
        var path = $"{RasGatesPath}/{rasGate.Id}/status/" +
                   (live ? "live" : "shadow");
        using var request = new HttpRequestMessage(
            live ? HttpMethod.Post : HttpMethod.Get,
            path);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ras_gate_inactive", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateBoundary.StatusRequestCount);
    }

    [Fact]
    public async Task Background_handlers_reject_inactive_gate_before_creating_requests()
    {
        var rasGate = await _factory.SeedRasGateAsync(isActive: false);
        using var scope = _factory.Services.CreateScope();
        var statusHandler = scope.ServiceProvider.GetRequiredService<
            IBackgroundTaskHandler<CheckRasGateStatusTask>>();
        var clustersHandler = scope.ServiceProvider.GetRequiredService<
            IBackgroundTaskHandler<
                SynchronizeClustersTask,
                CollectionSynchronizationResult>>();

        await Assert.ThrowsAsync<RasGateInactiveException>(() =>
            statusHandler.ExecuteAsync(
                new CheckRasGateStatusTask(rasGate.Id),
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<RasGateInactiveException>(() =>
            clustersHandler.ExecuteAsync(
                new SynchronizeClustersTask(rasGate.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, _factory.RasGateBoundary.StatusRequestCount);
        Assert.Equal(0, _factory.RasGateBoundary.ClusterRequestCount);
    }
}
