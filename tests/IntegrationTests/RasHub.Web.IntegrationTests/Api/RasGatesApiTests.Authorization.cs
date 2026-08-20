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

public sealed partial class RasGatesApiTests
{
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
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/ras-hub/status");
        request.Headers.TryAddWithoutValidation(
            ApiKeyAuthenticationDefaults.HeaderName,
            ["invalid", RasHubWebApplicationFactory.ApiKey]);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_api_key_cannot_mutate_RasGate_configuration()
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
        var request = new CreateRasGateRequest(
            "Forbidden gate",
            "https://gate.example.test",
            443,
            "gate-secret");

        using var response = await client.PostAsJsonAsync(
            RasGatesPath,
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", GetErrorCode(json));
        AssertTraceId(response);
    }

    [Fact]
    public async Task Non_admin_api_key_cannot_remove_cluster()
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

        using var response = await client.DeleteAsync(
            $"/api/v1/ras-gates/{Guid.NewGuid()}/clusters/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", GetErrorCode(json));
        AssertTraceId(response);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Non_admin_api_key_cannot_create_or_update_cluster(
        string method)
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
        var gateId = Guid.NewGuid();
        var path = $"/api/v1/ras-gates/{gateId}/clusters";
        object body;

        if (method == "PUT")
        {
            path += $"/{Guid.NewGuid()}";
            body = new UpdateClusterRequest("Updated");
        }
        else
        {
            body = new CreateClusterRequest("localhost", 1587);
        }

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(body)
        };
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", GetErrorCode(json));
        AssertTraceId(response);
    }
}