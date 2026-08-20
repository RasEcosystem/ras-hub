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

public sealed class RasGateGatewayTests
{
    [Fact]
    public async Task GetStatusAsync_inactive_gate_rejects_before_transport()
    {
        var requestCount = 0;
        using var httpClient = CreateHttpClient(_ =>
        {
            requestCount++;
            return JsonResponse("{}");
        });
        var rasGate = CreateRasGate(
            new Uri("https://gate.example.test/"),
            "gate-secret");
        rasGate.IsActive = false;
        var client = CreateClient(httpClient, rasGate);

        await Assert.ThrowsAsync<RasGateInactiveException>(() =>
            client.GetStatusAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task GetStatusAsync_invalid_endpoint_returns_sanitized_error()
    {
        using var httpClient = CreateHttpClient(_ => JsonResponse("{}"));
        var rasGate = new RasGate
        {
            Name = "Invalid Gate",
            Url = "not-an-endpoint",
            Port = 443,
            ApiKey = "gate-secret"
        };
        var client = CreateClient(httpClient, rasGate);

        var exception = await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.GetStatusAsync(TestContext.Current.CancellationToken));

        Assert.Contains(rasGate.Id.ToString(), exception.Message);
        Assert.IsType<RasGateEndpointValidationException>(exception.InnerException);
    }

    [Fact]
    public async Task GetStatusAsync_returns_normalized_status()
    {
        var requestedUris = new List<Uri>();
        var apiKeySent = new List<bool>();
        using var httpClient = CreateHttpClient(request =>
        {
            requestedUris.Add(Assert.IsType<Uri>(request.RequestUri));
            apiKeySent.Add(request.Headers.Contains("X-Api-Key"));

            if (request.RequestUri.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal))
                return RacStatusResponse("rac 8.3.27.2214");

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
        Assert.True(status.RacAvailable);
        Assert.Equal("8.3.27.2214", status.RacVersion);
        Assert.Equal(
            [
                "https://gate.example.test:8443/root/rasgate/status",
                "https://gate.example.test:8443/root/rac/status"
            ],
            requestedUris.Select(item => item.AbsoluteUri));
        Assert.Equal([false, false], apiKeySent);
    }

    [Fact]
    public async Task GetStatusAsync_unavailable_RAC_returns_degraded_status()
    {
        using var httpClient = CreateHttpClient(request =>
            request.RequestUri?.AbsolutePath.EndsWith(
                "/rac/status",
                StringComparison.Ordinal) == true
                ? RacUnavailableResponse()
                : RasGateStatusResponse());
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var status = await client.GetStatusAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("Remote Gate", status.InstanceName);
        Assert.Equal("1.2.3+abc", status.Version);
        Assert.False(status.RacAvailable);
        Assert.Null(status.RacVersion);
    }

    [Fact]
    public async Task GetStatusAsync_failed_RAC_probe_returns_unknown_and_logs_sanitized_warning()
    {
        var logger = new CollectingLogger<RasGateStatusGateway>();
        using var httpClient = CreateHttpClient(request =>
            request.RequestUri?.AbsolutePath.EndsWith(
                "/rac/status",
                StringComparison.Ordinal) == true
                ? JsonResponse("remote secret payload")
                : RasGateStatusResponse());
        var client = CreateClient(
            httpClient,
            CreateRasGate(
                new Uri("https://gate.example.test/"),
                "gate-secret"),
            statusLogger: logger);

        var status = await client.GetStatusAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(status.RacAvailable);
        Assert.Null(status.RacVersion);
        var warning = Assert.Single(logger.Messages);
        Assert.Contains("RAC status could not be observed", warning);
        Assert.DoesNotContain("remote secret payload", warning);
        Assert.DoesNotContain("gate-secret", warning);
    }

    [Fact]
    public async Task GetStatusAsync_cancelled_RAC_probe_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = CreateHttpClient(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal) == true)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            return RasGateStatusResponse();
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetStatusAsync(cancellation.Token));
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
                Headers = { Location = new Uri("https://attacker.example.test/") }
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
    public async Task GetInfobasesAsync_executes_summary_list_and_deserializes_output()
    {
        var clusterId = Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7");
        var infobaseId = Guid.Parse("85f82b58-d02c-4f40-9ad3-2131adf31e48");
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
            return RacExecutionResponse(
                "succeeded",
                0,
                false,
                $"infobase : {infobaseId:D}\n" +
                "name : rim_next\n" +
                "descr : \n");
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var snapshot = await client.GetInfobasesAsync(
            clusterId,
            "cluster-admin",
            "cluster-secret",
            TestContext.Current.CancellationToken);

        Assert.Equal(SnapshotCompleteness.Complete, snapshot.Completeness);
        Assert.Equal(infobaseId, Assert.Single(snapshot.Items).ExternalId);
        Assert.NotNull(requestJson);
        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal(
            [
                "infobase",
                "summary",
                "list",
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
    public async Task GetInfobaseAsync_executes_summary_info_and_returns_requested_infobase()
    {
        var clusterId = Guid.Parse("820d1955-349e-4173-9092-a3f206d328f7");
        var infobaseId = Guid.Parse("85f82b58-d02c-4f40-9ad3-2131adf31e48");
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
            return RacExecutionResponse(
                "succeeded",
                0,
                false,
                $"infobase : {infobaseId:D}\n" +
                "name : rim_next\n" +
                "descr : \n");
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var infobase = await client.GetInfobaseAsync(
            clusterId,
            infobaseId,
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(infobaseId, infobase.ExternalId);
        Assert.Equal("rim_next", infobase.Name);
        Assert.NotNull(requestJson);
        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal(
            [
                "infobase",
                "summary",
                "info",
                $"--cluster={clusterId:D}",
                $"--infobase={infobaseId:D}"
            ],
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
            return RacExecutionResponse(
                "succeeded",
                0,
                false,
                $"cluster : {clusterId:D}\r\n");
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var result = await client.CreateClusterAsync(
            new RasClusterCreationOptions { Host = "localhost", Port = 1587, Name = "Новый кластер" },
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
    public async Task CreateClusterAsync_unknown_outcome_rejects_mutation()
    {
        var rasGate = CreateRasGate(
            new Uri("https://gate.example.test/"),
            "gate-secret");
        using var httpClient = CreateRacHttpClient(() =>
            RacExecutionResponse("unknown", -1, true));
        var client = CreateClient(
            httpClient,
            rasGate);

        var exception = await Assert.ThrowsAsync<
            RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));

        Assert.Equal(rasGate.Id, exception.RasGateId);
        Assert.Equal("clusters", exception.Resource);
        Assert.Equal("insert", exception.Operation);
    }

    [Theory]
    [InlineData("rac_execution_outcome_unknown")]
    [InlineData("rac_output_limit_exceeded")]
    public async Task CreateClusterAsync_remote_unknown_error_rejects_mutation(
        string errorCode)
    {
        using var httpClient = CreateRacHttpClient(() =>
            RacErrorResponse(errorCode, HttpStatusCode.BadGateway));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("malformed_bad_gateway")]
    [InlineData("internal_server_error")]
    [InlineData("empty_gateway_timeout")]
    public async Task CreateClusterAsync_uncertain_server_error_reports_unknown(
        string failure)
    {
        using var httpClient = CreateRacHttpClient(() => failure switch
        {
            "malformed_bad_gateway" => JsonResponse(
                "{",
                HttpStatusCode.BadGateway),
            "internal_server_error" => RacErrorResponse(
                "internal_server_error",
                HttpStatusCode.InternalServerError),
            "empty_gateway_timeout" => JsonResponse(
                "",
                HttpStatusCode.GatewayTimeout),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "bad_request")]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData(HttpStatusCode.TooManyRequests, "rac_capacity_exceeded")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "rac_unavailable")]
    [InlineData(HttpStatusCode.OK, "bad_request")]
    [InlineData(HttpStatusCode.OK, "rac_unavailable")]
    public async Task CreateClusterAsync_confirmed_pre_start_error_remains_client_error(
        HttpStatusCode statusCode,
        string errorCode)
    {
        using var httpClient = CreateRacHttpClient(() =>
            RacErrorResponse(errorCode, statusCode));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var exception = await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));

        Assert.IsNotType<RasGateMutationOutcomeUnknownException>(exception);
    }

    [Theory]
    [InlineData("disconnect")]
    [InlineData("timeout")]
    public async Task CreateClusterAsync_post_dispatch_transport_failure_reports_unknown(
        string failure)
    {
        var rasGate = CreateRasGate(
            new Uri("https://gate.example.test/"),
            "gate-secret");
        using var httpClient = CreateRacHttpClient(() => failure switch
        {
            "disconnect" => throw new HttpRequestException("Connection lost."),
            "timeout" => throw new OperationCanceledException(),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        });
        var client = CreateClient(httpClient, rasGate);

        var exception = await Assert.ThrowsAsync<
            RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));

        Assert.Equal(rasGate.Id, exception.RasGateId);
        Assert.Equal("clusters", exception.Resource);
        Assert.Equal("insert", exception.Operation);
    }

    [Fact]
    public async Task CreateClusterAsync_cancellation_after_dispatch_reports_unknown()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = CreateHttpClient(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal) == true)
                return RacStatusResponse("8.3.27.2214");

            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                cancellation.Token));
    }

    [Fact]
    public async Task CreateClusterAsync_interrupted_response_body_reports_unknown()
    {
        using var httpClient = CreateRacHttpClient(() =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new InterruptingHttpContent() });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"success\":true,\"data\":null}")]
    public async Task CreateClusterAsync_invalid_or_empty_2xx_response_reports_unknown(
        string responseBody)
    {
        using var httpClient = CreateRacHttpClient(() =>
            JsonResponse(responseBody));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateClusterAsync_incomplete_execution_response_reports_unknown()
    {
        using var httpClient = CreateRacHttpClient(() => JsonResponse(
            """
            {
              "success": true,
              "data": {
                "outcome": "succeeded",
                "exitCode": 0,
                "standardOutput": "cluster : 8f5a6128-c013-4cd0-bd93-f4fd924d64c1\r\n",
                "standardError": "",
                "durationMilliseconds": null,
                "timedOut": false
              }
            }
            """));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateClusterAsync_version_error_before_dispatch_remains_client_error()
    {
        var executionRequestCount = 0;
        using var httpClient = CreateHttpClient(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal) == true)
                return RacStatusResponse("not-a-version");

            executionRequestCount++;
            return RacExecutionResponse("succeeded", 0, false);
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));

        Assert.Equal(0, executionRequestCount);
    }

    [Fact]
    public async Task CreateClusterAsync_cancellation_during_version_lookup_propagates()
    {
        var executionRequestCount = 0;
        using var cancellation = new CancellationTokenSource();
        using var httpClient = CreateHttpClient(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "/rac/status",
                    StringComparison.Ordinal) == true)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            executionRequestCount++;
            return RacExecutionResponse("succeeded", 0, false);
        });
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                cancellation.Token));

        Assert.Equal(0, executionRequestCount);
    }

    [Fact]
    public async Task CreateClusterAsync_cancellation_before_dispatch_propagates()
    {
        var statusRequestCount = 0;
        var executionRequestCount = 0;
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

            executionRequestCount++;
            return RacExecutionResponse("succeeded", 0, false);
        });
        var warmupClient = CreateClient(httpClient, rasGate, versionCache);
        await warmupClient.GetCapabilitiesAsync(
            TestContext.Current.CancellationToken);
        var client = CreateClient(httpClient, rasGate, versionCache);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                cancellation.Token));

        Assert.Equal(1, statusRequestCount);
        Assert.Equal(0, executionRequestCount);
    }

    [Fact]
    public async Task CreateClusterAsync_confirmed_failed_execution_remains_client_error()
    {
        using var httpClient = CreateRacHttpClient(() =>
            RacExecutionResponse("failed", 1, false));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        var exception = await Assert.ThrowsAsync<RasGateClientException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));

        Assert.IsNotType<RasGateMutationOutcomeUnknownException>(exception);
    }

    [Fact]
    public async Task CreateClusterAsync_inconsistent_succeeded_outcome_reports_unknown()
    {
        using var httpClient = CreateRacHttpClient(() =>
            RacExecutionResponse("succeeded", 1, false));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateClusterAsync_invalid_execution_outcome_reports_unknown()
    {
        using var httpClient = CreateRacHttpClient(() =>
            RacExecutionResponse("unexpected", 0, false));
        var client = CreateClient(
            httpClient,
            new Uri("https://gate.example.test/"),
            "gate-secret");

        await Assert.ThrowsAsync<RasGateMutationOutcomeUnknownException>(() =>
            client.CreateClusterAsync(
                new RasClusterCreationOptions { Host = "localhost", Port = 1587 },
                TestContext.Current.CancellationToken));
    }

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

    private static RasGateGatewayTestFacade CreateClient(
        HttpClient httpClient,
        Uri baseAddress,
        string apiKey,
        RacVersionCache? versionCache = null,
        ILogger<RasGateStatusGateway>? statusLogger = null)
    {
        return CreateClient(
            httpClient,
            CreateRasGate(baseAddress, apiKey),
            versionCache,
            statusLogger);
    }

    private static RasGateGatewayTestFacade CreateClient(
        HttpClient httpClient,
        RasGate rasGate,
        RacVersionCache? versionCache = null,
        ILogger<RasGateStatusGateway>? statusLogger = null)
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
        var infobaseDeserializer = new RacInfobaseOutputV1Deserializer(
            new RacKeyValueOutputDeserializer());
        var infobaseDeserializerResolver =
            new RacInfobaseOutputDeserializerResolver(
                [infobaseDeserializer]);
        IRacResultCommandAdapter<
            RacInfobaseQuery,
            RasResourceSnapshot<RasInfobaseSnapshot>>[] infobaseAdapters =
        [
            new RacInfobaseSnapshotV1Adapter(
                infobaseDeserializerResolver),
            new RacInfobaseInfoV1Adapter(
                infobaseDeserializerResolver)
        ];
        var descriptors = adapters
            .Cast<IRacResourceAdapterDescriptor>()
            .Concat(insertAdapters)
            .Concat(updateAdapters)
            .Concat(commandAdapters)
            .Concat(infobaseAdapters);

        var sessionFactory = new RasGateSessionFactory(
            httpClient,
            new RasGateEndpointFactory(),
            versionCache ?? new RacVersionCache(TimeProvider.System),
            new RacVersionParser(),
            new RacCapabilityResolver(descriptors));
        var statusGateway = new RasGateStatusGateway(
            sessionFactory,
            statusLogger ?? NullLogger<RasGateStatusGateway>.Instance);
        var clusterGateway = new RasClusterGateway(
            sessionFactory,
            new RacResourceAdapterResolver<RasClusterSnapshot>(adapters),
            new RacResultCommandAdapterResolver<
                RasClusterCreationOptions,
                Guid>(insertAdapters),
            new RacCommandAdapterResolver<UpdateRasClusterCommand>(
                updateAdapters),
            new RacCommandAdapterResolver<RemoveRasClusterCommand>(
                commandAdapters));
        var infobaseGateway = new RasInfobaseGateway(
            sessionFactory,
            new RacResultCommandAdapterResolver<
                RacInfobaseQuery,
                RasResourceSnapshot<RasInfobaseSnapshot>>(
                infobaseAdapters));

        return new RasGateGatewayTestFacade(
            rasGate,
            statusGateway,
            clusterGateway,
            infobaseGateway);
    }

    private static RasGate CreateRasGate(
        Uri baseAddress,
        string apiKey,
        long configurationRevision = 1)
    {
        return new RasGate
        {
            Name = "Test Gate",
            Url = baseAddress.AbsoluteUri,
            Port = baseAddress.Port,
            ApiKey = apiKey,
            ConfigurationRevision = configurationRevision
        };
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        return new HttpClient(new StubHttpMessageHandler(responseFactory));
    }

    private static HttpClient CreateUnknownOutcomeHttpClient()
    {
        return CreateRacHttpClient(() =>
            RacExecutionResponse("unknown", -1, true));
    }

    private static HttpClient CreateRacHttpClient(
        Func<HttpResponseMessage> executionResponseFactory)
    {
        return CreateHttpClient(request =>
            request.RequestUri?.AbsolutePath.EndsWith(
                "/rac/status",
                StringComparison.Ordinal) == true
                ? RacStatusResponse("8.3.27.2214")
                : executionResponseFactory());
    }

    private static HttpResponseMessage RacExecutionResponse(
        string outcome,
        int exitCode,
        bool timedOut,
        string standardOutput = "")
    {
        return JsonResponse(JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                outcome,
                exitCode,
                standardOutput,
                standardError = "",
                durationMilliseconds = 7,
                timedOut
            }
        }));
    }

    private static HttpResponseMessage RacErrorResponse(
        string errorCode,
        HttpStatusCode statusCode)
    {
        return JsonResponse(
            JsonSerializer.Serialize(new
            {
                success = false,
                error = new { code = errorCode, message = "remote implementation details" }
            }),
            statusCode);
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
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

    private static HttpResponseMessage RacUnavailableResponse()
    {
        return JsonResponse(
            """
            {
              "success": true,
              "data": {
                "available": false,
                "version": null,
                "message": "RAC executable was not found"
              }
            }
            """);
    }

    private static HttpResponseMessage RasGateStatusResponse()
    {
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
    }

    private sealed class RasGateGatewayTestFacade(
        RasGate rasGate,
        RasGateStatusGateway statusGateway,
        RasClusterGateway clusterGateway,
        RasInfobaseGateway infobaseGateway)
    {
        public Task<RasGateStatus> GetStatusAsync(
            CancellationToken cancellationToken)
        {
            return statusGateway.GetStatusAsync(rasGate, cancellationToken);
        }

        public Task<RasGateCapabilities> GetCapabilitiesAsync(
            CancellationToken cancellationToken)
        {
            return clusterGateway.GetCapabilitiesAsync(
                rasGate,
                cancellationToken);
        }

        public Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
            CancellationToken cancellationToken)
        {
            return clusterGateway.GetClustersAsync(rasGate, cancellationToken);
        }

        public Task<RasClusterSnapshot> GetClusterAsync(
            Guid clusterId,
            CancellationToken cancellationToken)
        {
            return clusterGateway.GetClusterAsync(
                rasGate,
                clusterId,
                cancellationToken);
        }

        public Task<RasResourceSnapshot<RasInfobaseSnapshot>> GetInfobasesAsync(
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            return infobaseGateway.GetInfobasesAsync(
                rasGate,
                clusterId,
                clusterUser,
                clusterPassword,
                cancellationToken);
        }

        public Task<RasInfobaseSnapshot> GetInfobaseAsync(
            Guid clusterId,
            Guid infobaseId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            return infobaseGateway.GetInfobaseAsync(
                rasGate,
                clusterId,
                infobaseId,
                clusterUser,
                clusterPassword,
                cancellationToken);
        }

        public Task<Guid> CreateClusterAsync(
            RasClusterCreationOptions options,
            CancellationToken cancellationToken)
        {
            return clusterGateway.CreateClusterAsync(
                rasGate,
                options,
                cancellationToken);
        }

        public Task UpdateClusterAsync(
            Guid clusterId,
            RasClusterUpdateOptions options,
            CancellationToken cancellationToken)
        {
            return clusterGateway.UpdateClusterAsync(
                rasGate,
                clusterId,
                options,
                cancellationToken);
        }

        public Task RemoveClusterAsync(
            Guid clusterId,
            string? clusterUser,
            string? clusterPassword,
            CancellationToken cancellationToken)
        {
            return clusterGateway.RemoveClusterAsync(
                rasGate,
                clusterId,
                clusterUser,
                clusterPassword,
                cancellationToken);
        }
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

    private sealed class InterruptingHttpContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            return Task.FromException(
                new IOException("The response body was interrupted."));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Messages.Add(formatter(state, exception));
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