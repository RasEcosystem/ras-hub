using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Domain;
using RasHub.Infrastructure.Database;

namespace RasHub.Infrastructure.IntegrationTests.Database;

public sealed class RasEndpointRegistryTests : IDisposable
{
    private readonly SqliteRasHubDatabase _database = new();

    public void Dispose()
    {
        _database.Dispose();
    }

    [Theory]
    [InlineData(" RAS.EXAMPLE.TEST. ", "ras.example.test")]
    [InlineData("[2001:0db8::1]", "2001:db8::1")]
    public async Task Register_normalizes_valid_host(
        string host,
        string expectedHost)
    {
        await using var db = _database.CreateContext();
        var registry = CreateRegistry(db);

        var endpoint = await registry.RegisterAsync(
            new RasEndpointRegistration(
                " Production ",
                host,
                1545,
                true),
            TestContext.Current.CancellationToken);

        Assert.Equal("Production", endpoint.Name);
        Assert.Equal(expectedHost, endpoint.Host);
        Assert.Equal(1545, endpoint.Port);
        Assert.Equal(1, endpoint.ConfigurationRevision);
    }

    [Theory]
    [InlineData("https://ras.example.test")]
    [InlineData("ras.example.test/path")]
    [InlineData("host name")]
    public async Task Register_rejects_non_host_value(string host)
    {
        await using var db = _database.CreateContext();
        var registry = CreateRegistry(db);

        await Assert.ThrowsAsync<RasEndpointAddressValidationException>(() =>
            registry.RegisterAsync(
                new RasEndpointRegistration("RAS", host, 1545, true),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_with_stale_revision_preserves_current_configuration()
    {
        await using var db = _database.CreateContext();
        var endpoint = RasEndpointTestData.Create();
        db.RasEndpoints.Add(endpoint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var registry = CreateRegistry(db);

        await registry.UpdateAsync(
            endpoint.Id,
            new RasEndpointRegistrationUpdate(
                "First update",
                endpoint.Host,
                endpoint.Port,
                endpoint.IsActive,
                endpoint.ConfigurationRevision),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<RasEndpointRevisionConflictException>(() =>
            registry.UpdateAsync(
                endpoint.Id,
                new RasEndpointRegistrationUpdate(
                    "Stale update",
                    endpoint.Host,
                    endpoint.Port,
                    endpoint.IsActive,
                    1),
                TestContext.Current.CancellationToken));

        Assert.Equal("First update", endpoint.Name);
        Assert.Equal(2, endpoint.ConfigurationRevision);
    }

    private static RasEndpointRegistry CreateRegistry(RasHubDbContext db)
    {
        return new RasEndpointRegistry(
            new EfRepository<RasEndpoint>(db),
            db);
    }
}
