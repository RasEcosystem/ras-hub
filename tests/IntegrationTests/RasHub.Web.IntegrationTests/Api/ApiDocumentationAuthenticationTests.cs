using System.Net;
using System.Text.Json;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Models.Search;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Contracts.RasHub.Requests.Infobases;
using RasHub.Contracts.RasHub.Responses;
using RasHub.Web.Authentication;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Api;

[Collection(WebApplicationCollection.Name)]
public sealed class ApiDocumentationAuthenticationTests
{
    private const string UserEmail = "documentation@example.test";
    private const string UserPassword = "Documentation-Password-42!";
    private static readonly string DisplayVersion = GetDisplayVersion();

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/openapi/v1.json")]
    public async Task Documentation_redirects_to_identity_login(string path)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Contains("ReturnUrl=", response.Headers.Location?.Query);
    }

    [Fact]
    public async Task Identity_login_uses_cosmic_rashub_branding()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            "/Account/Login?ReturnUrl=%2Fswagger%2F",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("documentation-login", html);
        Assert.Contains("brand-orbit", html);
        Assert.Contains("RasHub", html);
        Assert.Contains("Development Environment", html);
        Assert.Contains("login-divider", html);
        Assert.DoesNotContain($"v{DisplayVersion}", html);
        Assert.Contains("type=\"password\"", html);
        Assert.DoesNotContain("Log in with a passkey", html);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Application_login_is_labeled_as_administration_panel()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            "/Account/Login?ReturnUrl=%2F",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Administration Panel", html);
        Assert.DoesNotContain("Development Environment", html);
        Assert.Contains("login-divider", html);
        Assert.DoesNotContain($"v{DisplayVersion}", html);
    }

    private static string GetDisplayVersion()
    {
        var packageVersion = ThisAssembly.NuGetPackageVersion;
        var gitRevision = ThisAssembly.GitCommitId[..10];

        foreach (var separator in new[] { ".g", "-g" })
        {
            var suffix = $"{separator}{gitRevision}";

            if (packageVersion.EndsWith(suffix, StringComparison.Ordinal))
                return packageVersion[..^suffix.Length];
        }

        return packageVersion;
    }

    [Fact]
    public async Task Identity_user_unlocks_application_and_documentation()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);
        using var client = CreateClient(factory);

        using var login = await LoginAsync(client, UserEmail, UserPassword);

        Assert.True(
            login.StatusCode == HttpStatusCode.Redirect,
            await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("/swagger/", login.Headers.Location?.AbsolutePath);
        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(
            cookies,
            cookie => cookie.StartsWith(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal));

        using var scalar = await client.GetAsync(
            "/swagger/",
            TestContext.Current.CancellationToken);
        using var openApi = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var application = await client.GetAsync(
            "/",
            TestContext.Current.CancellationToken);
        using var engine = await client.GetAsync(
            "/background-tasks",
            TestContext.Current.CancellationToken);
        using var userSettings = await client.GetAsync(
            "/user-settings",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Equal(HttpStatusCode.OK, application.StatusCode);
        Assert.Equal(HttpStatusCode.OK, engine.StatusCode);
        Assert.Equal(HttpStatusCode.OK, userSettings.StatusCode);

        var document = await openApi.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            "/Account/",
            document,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Blocked_identity_user_cannot_log_in()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);
        await factory.SetIdentityUserBlockedAsync(UserEmail, true);
        using var client = CreateClient(factory);

        using var login = await LoginAsync(client, UserEmail, UserPassword);
        var html = await login.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Invalid login attempt", html);
        Assert.False(login.Headers.TryGetValues("Set-Cookie", out var cookies) &&
                     cookies.Any(cookie => cookie.StartsWith(
                         ".AspNetCore.Identity.Application=",
                         StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Blocking_user_revokes_existing_identity_cookie()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);
        using var client = CreateClient(factory);
        using var login = await LoginAsync(client, UserEmail, UserPassword);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        await factory.SetIdentityUserBlockedAsync(UserEmail, true);

        using var response = await client.GetAsync(
            "/swagger/",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Api_key_and_identity_cookie_keep_their_own_scopes()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);

        using var apiKeyClient = CreateClient(factory);
        apiKeyClient.DefaultRequestHeaders.Add(
            ApiKeyAuthenticationDefaults.HeaderName,
            RasHubWebApplicationFactory.ApiKey);
        using var documentationResponse = await apiKeyClient.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, documentationResponse.StatusCode);

        using var identityClient = CreateClient(factory);
        using var login = await LoginAsync(
            identityClient,
            UserEmail,
            UserPassword);
        using var apiResponse = await identityClient.GetAsync(
            "/api/v1/info",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);
    }

    [Fact]
    public async Task OpenApi_matches_published_http_contract()
    {
        using var factory = CreateFactory();
        await factory.SeedIdentityUserAsync(UserEmail, UserPassword);
        using var client = CreateClient(factory);
        using var login = await LoginAsync(client, UserEmail, UserPassword);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertOperation(root, "/api/v1/info", "get", "GetRasHubInfo", "RasHub", "200", "401");
        AssertOperation(root, "/api/v1/ras-gates", "get", "GetPagedRasGates", "RasGates", "200", "400", "401");
        AssertOperation(root, "/api/v1/ras-gates/all", "get", "GetAllRasGates", "RasGates", "200", "401");
        AssertResponseDataIsArray(root, "/api/v1/ras-gates/all", "get", "200");
        AssertOperation(root,
            "/api/v1/ras-gates/search",
            "get",
            "SearchPagedRasGates",
            "RasGates",
            "200",
            "400",
            "401");
        AssertOperation(root,
            "/api/v1/ras-gates/search/all",
            "get",
            "SearchAllRasGates",
            "RasGates",
            "200",
            "400",
            "401");
        AssertResponseDataIsArray(root, "/api/v1/ras-gates/search/all", "get", "200");
        AssertOperation(root, "/api/v1/ras-gates", "post", "RegisterRasGate", "RasGates", "201", "400", "401", "403");
        AssertOperation(root, "/api/v1/ras-gates/{rasGateId}", "get", "GetRasGate", "RasGates", "200", "401", "404");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}",
            "put",
            "UpdateRasGate",
            "RasGates",
            "200",
            "400",
            "401",
            "403",
            "404",
            "409");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}",
            "delete",
            "UnregisterRasGate",
            "RasGates",
            "200",
            "401",
            "403",
            "404",
            "409");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/status/shadow",
            "get",
            "GetShadowRasGateStatus",
            "RasGates",
            "200",
            "401",
            "404",
            "409");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/status/live",
            "post",
            "GetLiveRasGateStatus",
            "RasGates",
            "200",
            "401",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/shadow",
            "get",
            "GetShadowPagedClusters",
            "Clusters",
            "200",
            "400",
            "401",
            "404",
            "409");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/shadow/all",
            "get",
            "GetShadowAllClusters",
            "Clusters",
            "200",
            "401",
            "404",
            "409");
        AssertResponseDataIsArray(
            root,
            "/api/v1/ras-gates/{rasGateId}/clusters/shadow/all",
            "get",
            "200");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/shadow/{clusterId}",
            "get",
            "GetShadowCluster",
            "Clusters",
            "200",
            "401",
            "404",
            "409");
        AssertOperation(root,
            "/api/v1/clusters/shadow/search",
            "get",
            "SearchShadowPagedClusters",
            "Clusters",
            "200",
            "400",
            "401");
        AssertOperation(root,
            "/api/v1/clusters/shadow/search/all",
            "get",
            "SearchShadowAllClusters",
            "Clusters",
            "200",
            "400",
            "401");
        AssertResponseDataIsArray(
            root,
            "/api/v1/clusters/shadow/search/all",
            "get",
            "200");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters",
            "post",
            "CreateCluster",
            "Clusters",
            "201",
            "400",
            "401",
            "403",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/live",
            "post",
            "GetLivePagedClusters",
            "Clusters",
            "200",
            "400",
            "401",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/live/all",
            "post",
            "GetLiveAllClusters",
            "Clusters",
            "200",
            "401",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertResponseDataIsArray(
            root,
            "/api/v1/ras-gates/{rasGateId}/clusters/live/all",
            "post",
            "200");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/live/{clusterId}",
            "post",
            "GetLiveCluster",
            "Clusters",
            "200",
            "401",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/shadow/refresh",
            "post",
            "RefreshClusterShadow",
            "Clusters",
            "200",
            "401",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}",
            "patch",
            "UpdateCluster",
            "Clusters",
            "200",
            "400",
            "401",
            "403",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/remove",
            "post",
            "RemoveCluster",
            "Clusters",
            "200",
            "400",
            "401",
            "403",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/shadow",
            "get",
            "GetShadowPagedInfobases",
            "Infobases",
            "200",
            "400",
            "401",
            "404",
            "409");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/shadow/all",
            "get",
            "GetShadowAllInfobases",
            "Infobases",
            "200",
            "401",
            "404",
            "409");
        AssertResponseDataIsArray(
            root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/shadow/all",
            "get",
            "200");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/shadow/{infobaseId}",
            "get",
            "GetShadowInfobase",
            "Infobases",
            "200",
            "401",
            "404",
            "409");
        AssertOperation(root,
            "/api/v1/infobases/shadow/search",
            "get",
            "SearchShadowPagedInfobases",
            "Infobases",
            "200",
            "400",
            "401");
        AssertOperation(root,
            "/api/v1/infobases/shadow/search/all",
            "get",
            "SearchShadowAllInfobases",
            "Infobases",
            "200",
            "400",
            "401");
        AssertResponseDataIsArray(
            root,
            "/api/v1/infobases/shadow/search/all",
            "get",
            "200");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/live",
            "post",
            "GetLivePagedInfobases",
            "Infobases",
            "200",
            "400",
            "401",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/live/all",
            "post",
            "GetLiveAllInfobases",
            "Infobases",
            "200",
            "400",
            "401",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertResponseDataIsArray(
            root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/live/all",
            "post",
            "200");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/live/{infobaseId}",
            "post",
            "GetLiveInfobase",
            "Infobases",
            "200",
            "400",
            "401",
            "404",
            "409",
            "502",
            "503",
            "504");
        AssertOperation(root,
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/shadow/refresh",
            "post",
            "RefreshInfobaseShadow",
            "Infobases",
            "200",
            "400",
            "401",
            "404",
            "409",
            "502",
            "503",
            "504");

        Assert.False(paths.TryGetProperty("/api/v1/ras-hub/status", out _));
        Assert.False(paths.TryGetProperty(
            "/api/v1/ras-gates/{rasGateId}/status/check",
            out _));
        Assert.False(paths.TryGetProperty(
            "/api/v1/ras-gates/{rasGateId}/status/synchronize",
            out _));
        Assert.False(paths.TryGetProperty("/api/v1/ras-gates/get-paged", out _));
        Assert.False(paths.TryGetProperty(
            "/api/v1/ras-gates/{rasGateId}/clusters/get-paged",
            out _));
        Assert.False(paths.TryGetProperty(
            "/api/v1/ras-gates/{rasGateId}/clusters/{clusterId}/infobases/get-paged",
            out _));

        var documentedTags = root.GetProperty("tags")
            .EnumerateArray()
            .Select(tag => tag.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(documentedTags.SetEquals(
            ["RasHub", "RasGates", "Clusters", "Infobases"]));

        var schemas = root
            .GetProperty("components")
            .GetProperty("schemas");

        Assert.True(schemas.TryGetProperty(nameof(ClusterModel), out _));
        Assert.True(schemas.TryGetProperty(nameof(InfobaseModel), out _));
        Assert.True(schemas.TryGetProperty(nameof(ClusterSearchResultModel), out _));
        Assert.True(schemas.TryGetProperty(nameof(InfobaseSearchResultModel), out _));
        Assert.True(schemas.TryGetProperty(nameof(CreateClusterRequest), out _));
        Assert.True(schemas.TryGetProperty(nameof(UpdateClusterRequest), out _));
        Assert.True(schemas.TryGetProperty(nameof(RemoveClusterRequest), out _));
        Assert.True(schemas.TryGetProperty(nameof(InfobaseCredentialsRequest), out _));
        Assert.True(schemas.TryGetProperty(nameof(RasHubInfoResponse), out _));
        Assert.True(schemas.TryGetProperty(nameof(ShadowRefreshResponse), out _));
        Assert.True(schemas.TryGetProperty("OpenApiErrorResponse", out _));
        Assert.False(schemas.TryGetProperty("RasClusterModel", out _));
        Assert.False(schemas.TryGetProperty("RasInfobaseModel", out _));
        Assert.False(schemas.TryGetProperty("SynchronizeInfobaseRequest", out _));
        Assert.False(schemas.TryGetProperty("SynchronizeInfobasesRequest", out _));
        Assert.False(schemas.TryGetProperty("CollectionSynchronizationResponse", out _));
        Assert.False(schemas.TryGetProperty("RasHubStatusResponse", out _));
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        const string loginPath = "/Account/Login?ReturnUrl=%2Fswagger%2F";
        var token = await IdentityFormTestHelpers.GetAntiforgeryTokenAsync(
            client,
            loginPath);

        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", email),
            new KeyValuePair<string, string>("Input.Password", password),
            new KeyValuePair<string, string>("Input.RememberMe", "false"),
            new KeyValuePair<string, string>("_handler", "login"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]);

        return await client.PostAsync(
            loginPath,
            form,
            TestContext.Current.CancellationToken);
    }

    private static RasHubWebApplicationFactory CreateFactory()
    {
        return new RasHubWebApplicationFactory("Development");
    }

    private static JsonElement ResolveResponseSchema(
        JsonElement root,
        string path,
        string method,
        string statusCode)
    {
        var schema = GetResponseSchema(root, path, method, statusCode);

        while (schema.TryGetProperty("$ref", out var reference))
        {
            schema = root;

            foreach (var segment in reference.GetString()!
                         .Split('/', StringSplitOptions.RemoveEmptyEntries)
                         .Skip(1))
                schema = schema.GetProperty(segment);
        }

        return schema;
    }

    private static JsonElement GetResponseSchema(
        JsonElement root,
        string path,
        string method,
        string statusCode)
    {
        return root
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("responses")
            .GetProperty(statusCode)
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
    }

    private static void AssertResponseDataIsArray(
        JsonElement root,
        string path,
        string method,
        string statusCode)
    {
        var responseSchema = ResolveResponseSchema(
            root,
            path,
            method,
            statusCode);

        Assert.Contains(
            "array",
            responseSchema
                .GetProperty("properties")
                .GetProperty("data")
                .GetProperty("type")
                .EnumerateArray()
                .Select(type => type.GetString()));
    }

    private static void AssertOperation(
        JsonElement root,
        string path,
        string method,
        string operationId,
        string tag,
        params string[] responseCodes)
    {
        var operation = root
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method);

        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.Equal(
            [tag],
            operation.GetProperty("tags")
                .EnumerateArray()
                .Select(value => value.GetString()));

        var actualResponseCodes = operation
            .GetProperty("responses")
            .EnumerateObject()
            .Select(response => response.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(
            actualResponseCodes.SetEquals(responseCodes),
            $"Operation '{operationId}' documents [{string.Join(", ", actualResponseCodes.Order())}] instead of [{string.Join(", ", responseCodes.Order())}].");

        var successCodes = responseCodes
            .Where(responseCode => responseCode.StartsWith('2'))
            .ToArray();
        Assert.Single(successCodes);

        var successSchema = ResolveResponseSchema(
            root,
            path,
            method,
            successCodes[0]);
        Assert.True(
            successSchema.GetProperty("properties").TryGetProperty("data", out _),
            $"Operation '{operationId}' success schema does not expose data.");

        foreach (var responseCode in responseCodes.Except(successCodes))
        {
            var errorSchemaReference = GetResponseSchema(
                root,
                path,
                method,
                responseCode);
            Assert.Equal(
                "#/components/schemas/OpenApiErrorResponse",
                errorSchemaReference.GetProperty("$ref").GetString());

            var errorSchema = ResolveResponseSchema(
                root,
                path,
                method,
                responseCode);
            var errorProperties = errorSchema.GetProperty("properties");

            Assert.True(
                errorProperties.TryGetProperty("success", out _),
                $"Operation '{operationId}' HTTP {responseCode} schema does not expose success.");
            Assert.True(
                errorProperties.TryGetProperty("error", out _),
                $"Operation '{operationId}' HTTP {responseCode} schema does not expose error.");
            Assert.True(
                errorProperties.TryGetProperty("errors", out _),
                $"Operation '{operationId}' HTTP {responseCode} schema does not expose errors.");
            Assert.False(
                errorProperties.TryGetProperty("data", out _),
                $"Operation '{operationId}' HTTP {responseCode} schema exposes success data.");
        }
    }

    private static HttpClient CreateClient(
        RasHubWebApplicationFactory factory)
    {
        return factory.CreateIdentityClient();
    }
}
