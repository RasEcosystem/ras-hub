using System.Net;
using System.Text;
using System.Text.Json;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Client;
using RasHub.Infrastructure.RasGates.Rac;
using RasHub.Infrastructure.RasGates.Rac.Adapters;
using RasHub.Infrastructure.RasGates.Rac.Clusters;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Client;

public sealed class HttpRasGateClientTests
{
    [Fact]
    public async Task GetStatusAsync_returns_normalized_status()
    {
        Uri? requestedUri = null;
        var apiKeySent = false;
        using var httpClient = CreateHttpClient(request =>
        {
            requestedUri = request.RequestUri;
            apiKeySent = request.Headers.Contains("X-Api-Key");
            return JsonResponse(
                """
                {
                  "success": true,
                  "data": {
                    "instanceName": "Remote Gate",
                    "version": "1.2.3+abc"
                  }
                }
                """);
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test:8443/root/"),
            "gate-secret");

        var status = await client.GetStatusAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("Remote Gate", status.InstanceName);
        Assert.Equal("1.2.3+abc", status.Version);
        Assert.Equal(
            "https://gate.example.test:8443/root/rasgate/status",
            requestedUri?.AbsoluteUri);
        Assert.False(apiKeySent);
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("{\"success\":true,\"data\":null}")]
    [InlineData("{\"success\":false,\"error\":{\"code\":\"failed\"}}")]
    [InlineData("{\"success\":true,\"data\":{\"instanceName\":\"Remote Gate\"}}")]
    [InlineData("{\"success\":true,\"data\":{\"instanceName\":\"\",\"version\":\"1.0.0\"}}")]
    public async Task GetStatusAsync_rejects_invalid_contract(string json)
    {
        using var httpClient = CreateHttpClient(_ => JsonResponse(json));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.GetStatusAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStatusAsync_remote_error_preserves_error_code()
    {
        using var httpClient = CreateHttpClient(_ => JsonResponse(
            """
            {
              "success": false,
              "error": {
                "code": "gate_unavailable"
              }
            }
            """));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var exception = await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.GetStatusAsync(TestContext.Current.CancellationToken));

        Assert.Contains("gate_unavailable", exception.Message);
    }

    [Fact]
    public async Task GetStatusAsync_rejects_unsuccessful_http_response()
    {
        using var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.GetStatusAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStatusAsync_rejects_redirect_without_replaying_secret()
    {
        var requestCount = 0;
        using var httpClient = CreateHttpClient(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri("https://attacker.example.test/")
                }
            };
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.GetStatusAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task GetClustersAsync_executes_cluster_list_and_deserializes_output()
    {
        HttpMethod? requestedMethod = null;
        Uri? requestedUri = null;
        string? requestJson = null;
        string? apiKey = null;
        using var httpClient = CreateHttpClient(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal) == true)
                return RacStatusResponse("8.3.27.2214");

            requestedMethod = request.Method;
            requestedUri = request.RequestUri;
            apiKey = request.Headers.GetValues("X-Api-Key").Single();
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
                    "standardOutput": "cluster : 820d1955-349e-4173-9092-a3f206d328f7\r\nhost : WIN-P4BDRRBVMU8\r\nport : 1541\r\nname : \"Локальный кластер\"\r\nexpiration-timeout : 60\r\nlifetime-limit : 0\r\nmax-memory-size : 0\r\nmax-memory-time-limit : 0\r\nsecurity-level : 0\r\nsession-fault-tolerance-level : 0\r\nload-balancing-mode : performance\r\nerrors-count-threshold : 0\r\nkill-problem-processes : 1\r\nkill-by-memory-with-dump : 0\r\nallow-access-right-audit-events-recording : 0\r\nping-period : 0\r\nping-timeout : 0\r\nrestart-schedule : \r\n\r\n",
                    "standardError": "",
                    "durationMilliseconds": 74,
                    "timedOut": false
                  }
                }
                """);
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test:8443/root/"),
            "gate-secret");

        var snapshot = await client.GetClustersAsync(
            TestContext.Current.CancellationToken);
        var cluster = Assert.Single(snapshot.Items);

        Assert.Equal(SnapshotCompleteness.Complete, snapshot.Completeness);
        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal("8.3.27.2214", snapshot.SourceVersion);
        Assert.Equal(HttpMethod.Post, requestedMethod);
        Assert.Equal(
            "https://gate.example.test:8443/root/rac/execute",
            requestedUri?.AbsoluteUri);
        Assert.Equal("gate-secret", apiKey);
        Assert.NotNull(requestJson);
        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal(
            ["cluster", "list"],
            requestDocument.RootElement
                .GetProperty("arguments")
                .EnumerateArray()
                .Select(item => item.GetString()));
        Assert.Equal(
            Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7"),
            cluster.ExternalId);
        Assert.Equal("Локальный кластер", cluster.Name);
        Assert.Equal("WIN-P4BDRRBVMU8", cluster.Host);
        Assert.Equal(1541, cluster.Port);
        Assert.True(cluster.KillProblemProcesses);
    }

    [Fact]
    public async Task GetClusterAsync_executes_cluster_info_and_returns_requested_cluster()
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
                $$"""
                  {
                    "success": true,
                    "data": {
                      "exitCode": 0,
                      "standardOutput": "cluster : {{clusterId:D}}\r\nhost : localhost\r\nport : 15455\r\nname : \"RasCluster\"\r\nexpiration-timeout : 60\r\nlifetime-limit : 0\r\nmax-memory-size : 0\r\nmax-memory-time-limit : 0\r\nsecurity-level : 0\r\nsession-fault-tolerance-level : 0\r\nload-balancing-mode : performance\r\nerrors-count-threshold : 0\r\nkill-problem-processes : 1\r\n",
                      "standardError": "",
                      "durationMilliseconds": 12,
                      "timedOut": false
                    }
                  }
                  """);
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var cluster = await client.GetClusterAsync(
            clusterId,
            TestContext.Current.CancellationToken);

        Assert.Equal(clusterId, cluster.ExternalId);
        Assert.NotNull(requestJson);
        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal(
            ["cluster", "info", $"--cluster={clusterId:D}"],
            requestDocument.RootElement
                .GetProperty("arguments")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public async Task RemoveClusterAsync_executes_cluster_remove()
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

        await client.RemoveClusterAsync(
            clusterId,
            "cluster-admin",
            "cluster-secret",
            TestContext.Current.CancellationToken);

        Assert.NotNull(requestJson);
        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal(
            [
                "cluster",
                "remove",
                $"--cluster={clusterId:D}",
                "--cluster-user=cluster-admin",
                "--cluster-pwd=cluster-secret"
            ],
            requestDocument.RootElement
                .GetProperty("arguments")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public async Task CreateClusterAsync_executes_cluster_insert_and_returns_id()
    {
        var clusterId = Guid.Parse("8f5a6128-c013-4cd0-bd93-f4fd924d64c1");
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
                $$"""
                  {
                    "success": true,
                    "data": {
                      "exitCode": 0,
                      "standardOutput": "cluster : {{clusterId:D}}\r\n",
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

        var result = await client.CreateClusterAsync(
            new RasClusterCreationOptions
            {
                Host = "localhost",
                Port = 1587,
                Name = "Новый кластер"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(clusterId, result);
        Assert.NotNull(requestJson);
        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal(
            [
                "cluster",
                "insert",
                "--host=localhost",
                "--port=1587",
                "--name=Новый кластер"
            ],
            requestDocument.RootElement
                .GetProperty("arguments")
                .EnumerateArray()
                .Select(item => item.GetString()));
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
                new RasResourceCapability("clusters", "update", 1)
            ],
            capabilities.Resources);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_same_gate_revision_reuses_cached_RAC_version()
    {
        var requestCount = 0;
        var rasGateId = Guid.NewGuid();
        var versionCache = new RacVersionCache(TimeProvider.System);
        using var httpClient = CreateHttpClient(_ =>
        {
            requestCount++;
            return RacStatusResponse("8.3.27.2214");
        });

        var firstClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
            versionCache);
        var secondClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
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
        var rasGateId = Guid.NewGuid();
        var versionCache = new RacVersionCache(TimeProvider.System);
        using var httpClient = CreateHttpClient(_ =>
        {
            requestCount++;
            return RacStatusResponse("8.3.27.2214");
        });

        var firstClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
            versionCache);
        var secondClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            8,
            versionCache);

        await firstClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);
        await secondClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_expired_cached_RAC_version_refreshes_version()
    {
        var requestCount = 0;
        var rasGateId = Guid.NewGuid();
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        var versionCache = new RacVersionCache(timeProvider);
        using var httpClient = CreateHttpClient(_ =>
        {
            requestCount++;
            return RacStatusResponse("8.3.27.2214");
        });

        var firstClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
            versionCache);

        await firstClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        var secondClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
            versionCache);
        await secondClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetClustersAsync_parse_failure_invalidates_cached_RAC_version()
    {
        var statusRequestCount = 0;
        var rasGateId = Guid.NewGuid();
        var versionCache = new RacVersionCache(TimeProvider.System);
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
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
            versionCache);
        await firstClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        var secondClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
            versionCache);
        await Assert.ThrowsAsync<RacOutputDeserializationException>(() =>
            secondClient.GetClustersAsync(
                TestContext.Current.CancellationToken));

        var thirdClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
            versionCache);
        await thirdClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, statusRequestCount);
    }

    [Fact]
    public async Task CreateClusterAsync_parse_failure_invalidates_cached_RAC_version()
    {
        var statusRequestCount = 0;
        var rasGateId = Guid.NewGuid();
        var versionCache = new RacVersionCache(TimeProvider.System);
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
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
            versionCache);
        await firstClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);

        var secondClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
            versionCache);
        await Assert.ThrowsAsync<RasGateClientException>(() =>
            secondClient.CreateClusterAsync(
                new RasClusterCreationOptions
                {
                    Host = "localhost",
                    Port = 1587
                },
                TestContext.Current.CancellationToken));

        var thirdClient = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret",
            rasGateId,
            7,
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

    private static HttpRasGateClient CreateClient(
        HttpClient httpClient,
        Uri baseAddress,
        string apiKey,
        Guid? rasGateId = null,
        long configurationRevision = 1,
        RacVersionCache? versionCache = null)
    {
        var deserializer = new RacClusterOutputV1Deserializer(
            new RacKeyValueOutputDeserializer());
        var deserializerResolver = new RacClusterOutputDeserializerResolver(
            [deserializer]);
        IRacResourceAdapter<RasClusterSnapshot>[] adapters =
        [
            new RacClusterSnapshotV1Adapter(deserializerResolver),
            new RacClusterInfoV1Adapter(deserializerResolver)
        ];
        IRacCommandAdapter<RemoveRasClusterCommand>[] commandAdapters =
        [
            new RacClusterRemoveV1Adapter()
        ];
        IRacResultCommandAdapter<RasClusterCreationOptions, Guid>[]
            insertAdapters =
            [
                new RacClusterInsertV1Adapter(
                    new RacKeyValueOutputDeserializer())
            ];
        IRacCommandAdapter<UpdateRasClusterCommand>[] updateAdapters =
        [
            new RacClusterUpdateV1Adapter()
        ];
        var descriptors = adapters
            .Cast<IRacResourceAdapterDescriptor>()
            .Concat(insertAdapters)
            .Concat(updateAdapters)
            .Concat(commandAdapters);

        return new HttpRasGateClient(
            httpClient,
            baseAddress,
            apiKey,
            rasGateId ?? Guid.NewGuid(),
            configurationRevision,
            versionCache ?? new RacVersionCache(TimeProvider.System),
            new RacVersionParser(),
            new RacCapabilityResolver(descriptors),
            new RacResourceAdapterResolver<RasClusterSnapshot>(adapters),
            new RacResultCommandAdapterResolver<
                RasClusterCreationOptions,
                Guid>(insertAdapters),
            new RacCommandAdapterResolver<UpdateRasClusterCommand>(
                updateAdapters),
            new RacCommandAdapterResolver<RemoveRasClusterCommand>(
                commandAdapters));
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        return new HttpClient(new StubHttpMessageHandler(responseFactory));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage RacStatusResponse(string version)
    {
        return JsonResponse(
            $$"""
              {
                "success": true,
                "data": {
                  "available": true,
                  "version": "{{version}}",
                  "message": ""
                }
              }
              """);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            utcNow += duration;
        }
    }
}