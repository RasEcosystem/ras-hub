using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Domain;
using RasHub.Infrastructure.RasGates.Client;
using RasHub.Infrastructure.RasGates.Endpoints;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Commands;
using RasHub.Infrastructure.RasGates.Rac.Clusters.Deserialization;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Commands;
using RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Client;

public sealed partial class RasGateGatewayTests
{
    [Fact]
    public async Task GetClustersAsync_unknown_outcome_is_retryable_read_failure()
    {
        using var httpClient = CreateRacHttpClient(() =>
            RacExecutionResponse("unknown", -1, true));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var exception = await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.GetClustersAsync(TestContext.Current.CancellationToken));

        Assert.IsNotType<RasGateMutationOutcomeUnknownException>(exception);
    }

    [Fact]
    public async Task GetClustersAsync_malformed_2xx_response_remains_client_error()
    {
        using var httpClient = CreateRacHttpClient(() => JsonResponse("{"));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var exception = await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.GetClustersAsync(TestContext.Current.CancellationToken));

        Assert.IsNotType<RasGateMutationOutcomeUnknownException>(exception);
    }

    [Fact]
    public async Task UpdateClusterAsync_executes_cluster_update()
    {
        var clusterId = Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7");
        string? requestJson = null;
        using var httpClient = CreateHttpClient(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal) == true)
                return RacStatusResponse("8.3.27.2214");

            requestJson = request.Content?.ReadAsStringAsync(
                    TestContext.Current.CancellationToken)
                .GetAwaiter()
                .GetResult();
            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "exitCode": 0,
                    "standardOutput": "",
                    "standardError": "",
                    "durationMilliseconds": 7,
                    "timedOut": false
                  }
                }
                """);
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await client.UpdateClusterAsync(
            clusterId,
            new RasClusterUpdateOptions { Name = "Updated cluster" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(requestJson);
        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal(
            [
                "cluster",
                "update",
                $"--cluster={clusterId:D}",
                "--name=Updated cluster"
            ],
            requestDocument.RootElement
                .GetProperty("arguments")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public async Task UpdateClusterAsync_unknown_outcome_reports_update_operation()
    {
        using var httpClient = CreateUnknownOutcomeHttpClient();
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var exception = await Assert.ThrowsAsync<
            RasGateMutationOutcomeUnknownException>(() =>
            client.UpdateClusterAsync(
                Guid.NewGuid(),
                new RasClusterUpdateOptions { Name = "Updated cluster" },
                TestContext.Current.CancellationToken));

        Assert.Equal("clusters", exception.Resource);
        Assert.Equal("update", exception.Operation);
    }

    [Fact]
    public async Task RemoveClusterAsync_unknown_outcome_reports_remove_operation()
    {
        using var httpClient = CreateUnknownOutcomeHttpClient();
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var exception = await Assert.ThrowsAsync<
            RasGateMutationOutcomeUnknownException>(() =>
            client.RemoveClusterAsync(
                Guid.NewGuid(),
                null,
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal("clusters", exception.Resource);
        Assert.Equal("remove", exception.Operation);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task GetClustersAsync_rejects_failed_RAC_execution(
        int exitCode,
        bool timedOut)
    {
        using var httpClient = CreateHttpClient(request =>
            request.RequestUri?.AbsolutePath.EndsWith(
                "/rac/status",
                StringComparison.Ordinal) == true
                ? RacStatusResponse("8.3.27.2214")
                : JsonResponse($$"""
                                 {
                                   "success": true,
                                   "data": {
                                     "exitCode": {{exitCode}},
                                     "standardOutput": "",
                                     "standardError": "failed",
                                     "durationMilliseconds": 1,
                                     "timedOut": {{timedOut.ToString().ToLowerInvariant()}}
                                   }
                                 }
                                 """));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.GetClustersAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCapabilitiesAsync_returns_supported_resource_schemas()
    {
        using var httpClient = CreateHttpClient(_ =>
            RacStatusResponse("rac 8.3.27.2214"));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var capabilities = await client.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("8.3.27.2214", capabilities.RacVersion);
        Assert.Equal(
            [
                new RasResourceCapability("clusters", "info", 1),
                new RasResourceCapability("clusters", "insert", 1),
                new RasResourceCapability("clusters", "remove", 1),
                new RasResourceCapability("clusters", "snapshot", 1),
                new RasResourceCapability("clusters", "update", 1),
                new RasResourceCapability("infobases", "info", 1),
                new RasResourceCapability("infobases", "snapshot", 1)
            ],
            capabilities.Resources);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_unavailable_RAC_throws_typed_exception()
    {
        var rasGate = CreateRasGate(
            new Uri("https://gate.example.test/"),
            "gate-secret");
        using var httpClient = CreateHttpClient(_ => RacUnavailableResponse());
        var client = CreateClient(httpClient, rasGate);

        var exception = await Assert.ThrowsAsync<RacUnavailableException>(() =>
            client.GetCapabilitiesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(rasGate.Id, exception.RasGateId);
        Assert.DoesNotContain("gate-secret", exception.Message);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_same_gate_revision_reuses_cached_RAC_version()
    {
        var requestCount = 0;
        var versionCache = new RacVersionCache(TimeProvider.System);
        var rasGate = CreateRasGate(
            new Uri("https://gate.example.test/"),
            "gate-secret",
            7);
        using var httpClient = CreateHttpClient(_ =>
        {
            requestCount++;
            return RacStatusResponse("8.3.27.2214");
        });

        var firstClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);
        var secondClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);

        await firstClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);
        await secondClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_changed_gate_revision_refreshes_RAC_version()
    {
        var requestCount = 0;
        var versionCache = new RacVersionCache(TimeProvider.System);
        var rasGate = CreateRasGate(
            new Uri("https://gate.example.test/"),
            "gate-secret",
            7);
        using var httpClient = CreateHttpClient(_ =>
        {
            requestCount++;
            return RacStatusResponse("8.3.27.2214");
        });

        var firstClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);
        await firstClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        rasGate.ConfigurationRevision = 8;
        var secondClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);

        await secondClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_expired_cached_RAC_version_refreshes_version()
    {
        var requestCount = 0;
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        var versionCache = new RacVersionCache(timeProvider);
        var rasGate = CreateRasGate(
            new Uri("https://gate.example.test/"),
            "gate-secret",
            7);
        using var httpClient = CreateHttpClient(_ =>
        {
            requestCount++;
            return RacStatusResponse("8.3.27.2214");
        });

        var firstClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);

        await firstClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        var secondClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);
        await secondClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetClustersAsync_parse_failure_invalidates_cached_RAC_version()
    {
        var statusRequestCount = 0;
        var versionCache = new RacVersionCache(TimeProvider.System);
        var rasGate = CreateRasGate(
            new Uri("https://gate.example.test/"),
            "gate-secret",
            7);
        using var httpClient = CreateHttpClient(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal) == true)
            {
                statusRequestCount++;
                return RacStatusResponse("8.3.27.2214");
            }

            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "exitCode": 0,
                    "standardOutput": "cluster : not-a-guid\r\n",
                    "standardError": "",
                    "durationMilliseconds": 1,
                    "timedOut": false
                  }
                }
                """);
        });

        var firstClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);
        await firstClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        var secondClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);
        await Assert.ThrowsAsync<RacOutputDeserializationException>(() =>
            secondClient.GetClustersAsync(
                TestContext.Current.CancellationToken));

        var thirdClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);
        await thirdClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, statusRequestCount);
    }

    [Fact]
    public async Task CreateClusterAsync_parse_failure_invalidates_cached_RAC_version()
    {
        var statusRequestCount = 0;
        var versionCache = new RacVersionCache(TimeProvider.System);
        var rasGate = CreateRasGate(
            new Uri("https://gate.example.test/"),
            "gate-secret",
            7);
        using var httpClient = CreateHttpClient(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal) == true)
            {
                statusRequestCount++;
                return RacStatusResponse("8.3.27.2214");
            }

            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "exitCode": 0,
                    "standardOutput": "cluster : not-a-guid\r\n",
                    "standardError": "",
                    "durationMilliseconds": 1,
                    "timedOut": false
                  }
                }
                """);
        });

        var firstClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);
        await firstClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        var secondClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);
        var exception = await Assert.ThrowsAsync<
            RasGateMutationOutcomeUnknownException>(() =>
            secondClient.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));
        Assert.Equal(rasGate.Id, exception.RasGateId);
        Assert.Equal("clusters", exception.Resource);
        Assert.Equal("insert", exception.Operation);

        var thirdClient = CreateClient(
            httpClient,
            rasGate,
            versionCache);
        await thirdClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, statusRequestCount);
    }

    [Fact]
    public async Task GetClustersAsync_version_above_V1_minimum_executes_with_V1_adapter()
    {
        var requestCount = 0;
        using var httpClient = CreateHttpClient(request =>
        {
            requestCount++;

            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal) == true)
                return RacStatusResponse("8.4.0.1000");

            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "exitCode": 0,
                    "standardOutput": "cluster : 820d1955-349e-4173-9092-a3f206d328f7\r\nhost : localhost\r\nport : 1541\r\nname : \"Cluster\"\r\nexpiration-timeout : 60\r\nlifetime-limit : 0\r\nmax-memory-size : 0\r\nmax-memory-time-limit : 0\r\nsecurity-level : 0\r\nsession-fault-tolerance-level : 0\r\nload-balancing-mode : performance\r\nerrors-count-threshold : 0\r\nkill-problem-processes : 1\r\n",
                    "standardError": "",
                    "durationMilliseconds": 1,
                    "timedOut": false
                  }
                }
                """);
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var snapshot = await client.GetClustersAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("8.4.0.1000", snapshot.SourceVersion);
        Assert.Single(snapshot.Items);
        Assert.Equal(2, requestCount);
    }
}
