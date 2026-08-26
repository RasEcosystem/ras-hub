using System.Net;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Client;

namespace RasHub.Infrastructure.UnitTests.RasGates.Client;

public sealed partial class RasGateGatewayTests
{
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
}
