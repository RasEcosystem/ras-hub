using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RasHub.Application.RasGates.Models;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Web.Authentication;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Api;

[Collection(WebApplicationCollection.Name)]
public sealed class RasGatesApiTests : IClassFixture<RasHubWebApplicationFactory>
{
    private const string RasGatesPath = "/api/v1/ras-gates";

    private readonly RasHubWebApplicationFactory _factory;

    public RasGatesApiTests(RasHubWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task Request_without_api_key_returns_standard_unauthorized_response()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/ras-hub/status",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("unauthorized", GetErrorCode(json));
        AssertTraceId(response);
    }

    [Fact]
    public async Task Request_with_invalid_or_multiple_api_keys_is_rejected()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ras-hub/status");
        request.Headers.TryAddWithoutValidation(
            ApiKeyAuthenticationDefaults.HeaderName,
            ["invalid", RasHubWebApplicationFactory.ApiKey]);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_returns_created_location_and_does_not_expose_api_key()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var request = new CreateRasGateRequest(
            "Main gate",
            "https://main.example.test",
            8443,
            "top-secret");

        using var response = await client.PostAsJsonAsync(
            RasGatesPath,
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");
        var id = data.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(request.Name, data.GetProperty("name").GetString());
        Assert.Equal(request.Url, data.GetProperty("url").GetString());
        Assert.Equal(request.Port, data.GetProperty("port").GetInt32());
        Assert.False(data.TryGetProperty("apiKey", out _));
        Assert.EndsWith($"{RasGatesPath}/{id}", response.Headers.Location?.AbsoluteUri);
        AssertTraceId(response);

        var stored = await _factory.FindRasGateAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(request.ApiKey, stored.ApiKey);
    }

    [Fact]
    public async Task Get_by_id_returns_entity_without_api_key()
    {
        var rasGate = await _factory.SeedRasGateAsync(
            "Main gate",
            "https://main.example.test",
            8443,
            "top-secret");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(rasGate.Id, data.GetProperty("id").GetGuid());
        Assert.Equal(rasGate.Name, data.GetProperty("name").GetString());
        Assert.False(data.TryGetProperty("apiKey", out _));
    }

    [Fact]
    public async Task Get_by_unknown_id_returns_domain_specific_not_found_response()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var id = Guid.NewGuid();

        using var response = await client.GetAsync(
            $"{RasGatesPath}/{id}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("ras_gate_not_found", GetErrorCode(json));
        Assert.Contains(id.ToString(), json.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Get_status_without_refresh_returns_cached_status()
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
    public async Task Get_status_with_refresh_calls_gate_persists_and_returns_status()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        _factory.RasGateClientFactory.Status =
            new RasGateStatus(
                "Remote Gate",
                "2.3.4");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}/status?refresh=true",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Remote Gate", data.GetProperty("instanceName").GetString());
        Assert.Equal("2.3.4", data.GetProperty("version").GetString());
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("observedAt").ValueKind);
        Assert.Equal(1, _factory.RasGateClientFactory.StatusRequestCount);

        var stored = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(stored);
        Assert.Equal("Remote Gate", stored.InstanceName);
        Assert.Equal("2.3.4", stored.Version);
        Assert.NotNull(stored.StatusObservedAt);

        using var cachedResponse = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}/status",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.Equal(1, _factory.RasGateClientFactory.StatusRequestCount);
    }

    [Fact]
    public async Task Get_status_for_unknown_gate_returns_not_found_without_scheduling_refresh()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"{RasGatesPath}/{Guid.NewGuid()}/status?refresh=true",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ras_gate_not_found", GetErrorCode(json));
        Assert.Equal(0, _factory.RasGateClientFactory.StatusRequestCount);
    }

    [Fact]
    public async Task Update_without_api_key_preserves_the_stored_secret()
    {
        var rasGate = await _factory.SeedRasGateAsync(apiKey: "original-secret");
        using var client = _factory.CreateAuthenticatedClient();
        var request = new UpdateRasGateRequest(
            "Updated gate",
            "https://updated.example.test",
            9443);

        using var response = await client.PutAsJsonAsync(
            $"{RasGatesPath}/{rasGate.Id}",
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(request.Name, json.GetProperty("data").GetProperty("name").GetString());

        var stored = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(stored);
        Assert.Equal("original-secret", stored.ApiKey);
        Assert.Equal(request.Name, stored.Name);
        Assert.Equal(request.Url, stored.Url);
        Assert.Equal(request.Port, stored.Port);
    }

    [Fact]
    public async Task Update_with_api_key_replaces_the_stored_secret()
    {
        var rasGate = await _factory.SeedRasGateAsync(apiKey: "original-secret");
        using var client = _factory.CreateAuthenticatedClient();
        var request = new UpdateRasGateRequest(
            "Updated gate",
            "https://updated.example.test",
            9443,
            "new-secret");

        using var response = await client.PutAsJsonAsync(
            $"{RasGatesPath}/{rasGate.Id}",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("new-secret", (await _factory.FindRasGateAsync(rasGate.Id))?.ApiKey);
    }

    [Fact]
    public async Task Update_with_empty_api_key_is_rejected_and_preserves_the_stored_secret()
    {
        var rasGate = await _factory.SeedRasGateAsync(apiKey: "original-secret");
        using var client = _factory.CreateAuthenticatedClient();
        var request = new UpdateRasGateRequest(
            "Updated gate",
            "https://updated.example.test",
            9443,
            string.Empty);

        using var response = await client.PutAsJsonAsync(
            $"{RasGatesPath}/{rasGate.Id}",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("original-secret", (await _factory.FindRasGateAsync(rasGate.Id))?.ApiKey);
    }

    [Fact]
    public async Task Update_unknown_entity_returns_not_found()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var request = new UpdateRasGateRequest(
            "Updated gate",
            "https://updated.example.test",
            9443);

        using var response = await client.PutAsJsonAsync(
            $"{RasGatesPath}/{Guid.NewGuid()}",
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ras_gate_not_found", GetErrorCode(json));
    }

    [Fact]
    public async Task Delete_soft_deletes_entity_and_subsequent_get_returns_not_found()
    {
        var rasGate = await _factory.SeedRasGateAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var deleteResponse = await client.DeleteAsync(
            $"{RasGatesPath}/{rasGate.Id}",
            TestContext.Current.CancellationToken);
        var deleteJson = await ReadJsonAsync(deleteResponse);

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.False(deleteJson.GetProperty("data").TryGetProperty("apiKey", out _));

        var stored = await _factory.FindRasGateAsync(rasGate.Id);
        Assert.NotNull(stored);
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAt);

        using var getResponse = await client.GetAsync(
            $"{RasGatesPath}/{rasGate.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_unknown_entity_returns_not_found()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync(
            $"{RasGatesPath}/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ras_gate_not_found", GetErrorCode(json));
    }

    [Theory]
    [InlineData("", "https://gate.example.test", 443, "secret")]
    [InlineData("Gate", "", 443, "secret")]
    [InlineData("Gate", "https://gate.example.test", 0, "secret")]
    [InlineData("Gate", "https://gate.example.test", 65_536, "secret")]
    [InlineData("Gate", "https://gate.example.test", 443, "")]
    [InlineData("Gate", "https://gate.example.test", 443, null)]
    public async Task Create_rejects_invalid_request(
        string name,
        string url,
        int port,
        string? apiKey)
    {
        using var client = _factory.CreateAuthenticatedClient();
        var request = new CreateRasGateRequest(name, url, port, apiKey!);

        using var response = await client.PostAsJsonAsync(
            RasGatesPath,
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("bad_request", GetErrorCode(json));
        Assert.NotEmpty(json.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task Create_rejects_fields_over_their_maximum_lengths()
    {
        var requests = new[]
        {
            new CreateRasGateRequest(new string('n', 201), "https://gate.test", 443, "secret"),
            new CreateRasGateRequest("Gate", new string('u', 2_049), 443, "secret"),
            new CreateRasGateRequest("Gate", "https://gate.test", 443, new string('k', 513))
        };
        using var client = _factory.CreateAuthenticatedClient();

        foreach (var request in requests)
        {
            using var response = await client.PostAsJsonAsync(
                RasGatesPath,
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Get_paged_returns_pagination_metadata_and_never_exposes_api_keys()
    {
        await _factory.SeedRasGateAsync("First", apiKey: "first-secret");
        await _factory.SeedRasGateAsync("Second", apiKey: "second-secret");
        await _factory.SeedRasGateAsync("Third", apiKey: "third-secret");
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"{RasGatesPath}/get-paged",
            new PageRequest(1, 2),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(2, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, data.GetProperty("totalPages").GetInt32());
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.False(item.TryGetProperty("apiKey", out _)));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Get_paged_rejects_invalid_page_request(int page, int pageSize)
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"{RasGatesPath}/get-paged",
            new PageRequest(page, pageSize),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_api_route_returns_standard_not_found_response()
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            "/api/v1/unknown",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("not_found", GetErrorCode(json));
        AssertTraceId(response);
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

    private static string? GetErrorCode(JsonElement json)
    {
        return json.GetProperty("error").GetProperty("code").GetString();
    }

    private static void AssertTraceId(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("X-Trace-Id", out var values));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(values)));
    }
}