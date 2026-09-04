using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Web.Authentication;
using RasHub.Web.Data;
using RasHub.Web.IntegrationTests.Infrastructure;
using static RasHub.Web.IntegrationTests.Api.ApiResponseTestHelpers;

namespace RasHub.Web.IntegrationTests.Api;

[Collection(WebApplicationCollection.Name)]
public sealed class RasEndpointsApiTests
    : IClassFixture<RasHubWebApplicationFactory>
{
    private const string RasEndpointsPath = "/api/v1/ras-endpoints";

    private readonly RasHubWebApplicationFactory _factory;

    public RasEndpointsApiTests(RasHubWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task Create_returns_normalized_endpoint_and_location()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var request = new CreateRasEndpointRequest(
            "Production RAS",
            " RAS.EXAMPLE.TEST. ",
            1545);

        using var response = await client.PostAsJsonAsync(
            RasEndpointsPath,
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");
        var id = data.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(request.Name, data.GetProperty("name").GetString());
        Assert.Equal("ras.example.test", data.GetProperty("host").GetString());
        Assert.Equal(request.Port, data.GetProperty("port").GetInt32());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, data.GetProperty("configurationRevision").GetInt64());
        Assert.EndsWith(
            $"{RasEndpointsPath}/{id}",
            response.Headers.Location?.AbsoluteUri);
        AssertTraceId(response);

        var stored = await _factory.FindRasEndpointAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("ras.example.test", stored.Host);
    }

    [Fact]
    public async Task Get_by_id_and_collections_return_persisted_endpoints()
    {
        var first = await _factory.SeedRasEndpointAsync(
            "First",
            "first.example.test");
        await _factory.SeedRasEndpointAsync(
            "Second",
            "second.example.test");
        await _factory.SeedRasEndpointAsync(
            "Third",
            "third.example.test");
        using var client = _factory.CreateAuthenticatedClient();

        using var getResponse = await client.GetAsync(
            $"{RasEndpointsPath}/{first.Id}",
            TestContext.Current.CancellationToken);
        var getJson = await ReadJsonAsync(getResponse);
        using var pageResponse = await client.GetAsync(
            $"{RasEndpointsPath}?page=1&pageSize=2",
            TestContext.Current.CancellationToken);
        var pageJson = await ReadJsonAsync(pageResponse);
        using var allResponse = await client.GetAsync(
            $"{RasEndpointsPath}/all",
            TestContext.Current.CancellationToken);
        var allJson = await ReadJsonAsync(allResponse);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(
            first.Id,
            getJson.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal(
            3,
            pageJson.GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Equal(
            2,
            pageJson.GetProperty("data").GetProperty("items")
                .GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        Assert.Equal(3, allJson.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Update_replaces_configuration_and_advances_revision()
    {
        var endpoint = await _factory.SeedRasEndpointAsync();
        using var client = _factory.CreateAuthenticatedClient();
        var request = new UpdateRasEndpointRequest(
            "Standby RAS",
            "standby.example.test",
            2545,
            false,
            endpoint.ConfigurationRevision);

        using var response = await client.PutAsJsonAsync(
            $"{RasEndpointsPath}/{endpoint.Id}",
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);
        var data = json.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(request.Name, data.GetProperty("name").GetString());
        Assert.Equal(request.Host, data.GetProperty("host").GetString());
        Assert.Equal(request.Port, data.GetProperty("port").GetInt32());
        Assert.False(data.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, data.GetProperty("configurationRevision").GetInt64());

        var stored = await _factory.FindRasEndpointAsync(endpoint.Id);
        Assert.NotNull(stored);
        Assert.Equal(request.Name, stored.Name);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task Update_with_stale_revision_returns_conflict()
    {
        var endpoint = await _factory.SeedRasEndpointAsync();
        using var client = _factory.CreateAuthenticatedClient();
        var originalRevision = endpoint.ConfigurationRevision;

        using var firstResponse = await client.PutAsJsonAsync(
            $"{RasEndpointsPath}/{endpoint.Id}",
            new UpdateRasEndpointRequest(
                "First update",
                endpoint.Host,
                endpoint.Port,
                true,
                originalRevision),
            TestContext.Current.CancellationToken);
        using var staleResponse = await client.PutAsJsonAsync(
            $"{RasEndpointsPath}/{endpoint.Id}",
            new UpdateRasEndpointRequest(
                "Stale update",
                endpoint.Host,
                endpoint.Port,
                true,
                originalRevision),
            TestContext.Current.CancellationToken);
        var staleJson = await ReadJsonAsync(staleResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(
            "ras_endpoint_concurrency_conflict",
            GetErrorCode(staleJson));
        Assert.Equal(
            "First update",
            (await _factory.FindRasEndpointAsync(endpoint.Id))?.Name);
    }

    [Fact]
    public async Task Delete_soft_deletes_and_hides_endpoint()
    {
        var endpoint = await _factory.SeedRasEndpointAsync();
        using var client = _factory.CreateAuthenticatedClient();

        using var deleteResponse = await client.DeleteAsync(
            $"{RasEndpointsPath}/{endpoint.Id}",
            TestContext.Current.CancellationToken);
        using var getResponse = await client.GetAsync(
            $"{RasEndpointsPath}/{endpoint.Id}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        var stored = await _factory.FindRasEndpointAsync(endpoint.Id);
        Assert.NotNull(stored);
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(2, stored.ConfigurationRevision);
    }

    [Theory]
    [InlineData("RAS", "https://ras.example.test", 1545)]
    [InlineData("RAS", "ras.example.test/path", 1545)]
    [InlineData("RAS", "host name", 1545)]
    public async Task Create_rejects_invalid_host(
        string name,
        string host,
        int port)
    {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            RasEndpointsPath,
            new CreateRasEndpointRequest(name, host, port),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ras_endpoint_address_invalid", GetErrorCode(json));
    }

    [Fact]
    public async Task Unknown_endpoint_returns_domain_specific_not_found()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var id = Guid.NewGuid();

        using var getResponse = await client.GetAsync(
            $"{RasEndpointsPath}/{id}",
            TestContext.Current.CancellationToken);
        using var updateResponse = await client.PutAsJsonAsync(
            $"{RasEndpointsPath}/{id}",
            new UpdateRasEndpointRequest(
                "Unknown",
                "unknown.example.test",
                1545,
                true,
                1),
            TestContext.Current.CancellationToken);
        using var deleteResponse = await client.DeleteAsync(
            $"{RasEndpointsPath}/{id}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Non_admin_api_key_cannot_mutate_endpoint()
    {
        var apiKey = $"non-admin-{Guid.NewGuid():N}";
        var email = $"non-admin-{Guid.NewGuid():N}@example.test";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var result = await userManager.CreateAsync(new ApplicationUser
            {
                UserName = email,
                Email = email,
                ApiKey = apiKey
            });
            Assert.True(result.Succeeded);
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            ApiKeyAuthenticationDefaults.HeaderName,
            apiKey);

        using var response = await client.PostAsJsonAsync(
            RasEndpointsPath,
            new CreateRasEndpointRequest(
                "Forbidden RAS",
                "ras.example.test",
                1545),
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", GetErrorCode(json));
        AssertTraceId(response);
    }
}
