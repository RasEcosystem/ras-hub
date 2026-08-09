using System.Net;
using System.Text;
using System.Text.Json;
using RasHub.Application.RasGates.Exceptions;
using RasHub.Infrastructure.RasGates;
using RasHub.Infrastructure.RasGates.Serialization;

namespace RasHub.Infrastructure.UnitTests.RasGates;

public sealed class HttpRasGateClientTests
{
    [Fact]
    public async Task GetStatusAsync_returns_normalized_status()
    {
        Uri? requestedUri = null;
        string? apiKey = null;
        using var httpClient = CreateHttpClient(request =>
        {
            requestedUri = request.RequestUri;
            apiKey = request.Headers.GetValues("X-Api-Key").Single();
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
        Assert.Equal("gate-secret", apiKey);
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("{\"success\":true,\"data\":null}")]
    [InlineData("{\"success\":false,\"error\":{\"code\":\"failed\"}}")]
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
    public async Task GetClustersAsync_executes_cluster_list_and_deserializes_output()
    {
        HttpMethod? requestedMethod = null;
        Uri? requestedUri = null;
        string? requestJson = null;
        using var httpClient = CreateHttpClient(request =>
        {
            requestedMethod = request.Method;
            requestedUri = request.RequestUri;
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

        var clusters = await client.GetClustersAsync(
            TestContext.Current.CancellationToken);
        var cluster = Assert.Single(clusters);

        Assert.Equal(HttpMethod.Post, requestedMethod);
        Assert.Equal(
            "https://gate.example.test:8443/root/rac/execute",
            requestedUri?.AbsoluteUri);
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

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task GetClustersAsync_rejects_failed_RAC_execution(
        int exitCode,
        bool timedOut)
    {
        using var httpClient = CreateHttpClient(_ => JsonResponse(
            $$"""
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

    private static HttpRasGateClient CreateClient(
        HttpClient httpClient,
        Uri baseAddress,
        string apiKey)
    {
        return new HttpRasGateClient(
            httpClient,
            baseAddress,
            apiKey,
            new RacClusterOutputDeserializer(
                new RacKeyValueOutputDeserializer()));
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
}