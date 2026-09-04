using System.Text.Json;
using RasHub.Application.RasGates.Models;

namespace RasHub.Infrastructure.UnitTests.RasGates.Client;

public sealed partial class RasGateGatewayTests
{
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
            ["cluster", "list", "ras.example.test:1545"],
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
            [
                "cluster",
                "info",
                $"--cluster={clusterId:D}",
                "ras.example.test:1545"
            ],
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
                "--cluster-pwd=cluster-secret",
                "ras.example.test:1545"
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
                $"--infobase={infobaseId:D}",
                "ras.example.test:1545"
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
                "--cluster-pwd=cluster-secret",
                "ras.example.test:1545"
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
                "--name=Новый кластер",
                "ras.example.test:1545"
            ],
            requestDocument.RootElement
                .GetProperty("arguments")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }
}
