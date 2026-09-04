using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RasHub.Application.RasEndpoints.Models;
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
            CreateExecutionTarget(rasGate),
            statusGateway,
            clusterGateway,
            infobaseGateway);
    }

    private static RasEndpointExecutionTarget CreateExecutionTarget(
        RasGate rasGate)
    {
        var endpoint = new RasEndpoint
        {
            Name = "Test RAS endpoint",
            RasGateId = rasGate.Id,
            Host = "ras.example.test",
            Port = 1545
        };

        return new RasEndpointExecutionTarget(endpoint, rasGate);
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
        RasEndpointExecutionTarget target,
        RasGateStatusGateway statusGateway,
        RasClusterGateway clusterGateway,
        RasInfobaseGateway infobaseGateway)
    {
        public Task<RasGateStatus> GetStatusAsync(
            CancellationToken cancellationToken)
        {
            return statusGateway.GetStatusAsync(target.Gate, cancellationToken);
        }

        public Task<RasGateCapabilities> GetCapabilitiesAsync(
            CancellationToken cancellationToken)
        {
            return clusterGateway.GetCapabilitiesAsync(
                target.Gate,
                cancellationToken);
        }

        public Task<RasResourceSnapshot<RasClusterSnapshot>> GetClustersAsync(
            CancellationToken cancellationToken)
        {
            return clusterGateway.GetClustersAsync(target, cancellationToken);
        }

        public Task<RasClusterSnapshot> GetClusterAsync(
            Guid clusterId,
            CancellationToken cancellationToken)
        {
            return clusterGateway.GetClusterAsync(
                target,
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
                target,
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
                target,
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
                target,
                options,
                cancellationToken);
        }

        public Task UpdateClusterAsync(
            Guid clusterId,
            RasClusterUpdateOptions options,
            CancellationToken cancellationToken)
        {
            return clusterGateway.UpdateClusterAsync(
                target,
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
                target,
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
