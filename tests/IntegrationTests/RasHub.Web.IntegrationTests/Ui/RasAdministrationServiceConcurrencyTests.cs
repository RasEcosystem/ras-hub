using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Application.RasGates.Abstractions;
using RasHub.Application.RasGates.Services;
using RasHub.Infrastructure.Database;
using RasHub.Infrastructure.Database.Queries;
using RasHub.Web.Api.RasGates;
using RasHub.Web.Infrastructure.Authorization;
using RasHub.Web.Infrastructure.RasEndpoints;
using RasHub.Web.Infrastructure.RasGates;
using RasHub.Web.IntegrationTests.Infrastructure;

namespace RasHub.Web.IntegrationTests.Ui;

[Collection(WebApplicationCollection.Name)]
public sealed class RasAdministrationServiceConcurrencyTests
{
    [Fact]
    public async Task Update_gate_after_database_conflict_recovers_same_service_scope()
    {
        using var factory = new RasHubWebApplicationFactory();
        var gate = await factory.SeedRasGateAsync();
        using var serviceScope = factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider
            .GetRequiredService<RasHubDbContext>();
        _ = await db.RasGates.SingleAsync(
            item => item.Id == gate.Id,
            TestContext.Current.CancellationToken);

        using (var concurrentScope = factory.Services.CreateScope())
        {
            var concurrentDb = concurrentScope.ServiceProvider
                .GetRequiredService<RasHubDbContext>();
            var concurrentGate = await concurrentDb.RasGates.SingleAsync(
                item => item.Id == gate.Id,
                TestContext.Current.CancellationToken);
            concurrentGate.Name = "Concurrent Gate";
            await concurrentDb.SaveChangesAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal(2, concurrentGate.ConfigurationRevision);
        }

        var service = CreateGateService(serviceScope.ServiceProvider);
        var staleResult = await service.UpdateAsync(
            gate.Id,
            1,
            new RasGateEditorValues(
                "Stale Gate",
                "https://gate.example.test",
                443,
                null,
                true),
            TestContext.Current.CancellationToken);

        Assert.False(staleResult.Succeeded);
        Assert.Empty(db.ChangeTracker.Entries());

        var recoveredResult = await service.UpdateAsync(
            gate.Id,
            2,
            new RasGateEditorValues(
                "Recovered Gate",
                "https://gate.example.test",
                443,
                null,
                true),
            TestContext.Current.CancellationToken);

        Assert.True(recoveredResult.Succeeded);
        var stored = await factory.FindRasGateAsync(gate.Id);
        Assert.NotNull(stored);
        Assert.Equal("Recovered Gate", stored.Name);
        Assert.Equal(3, stored.ConfigurationRevision);
    }

    [Fact]
    public async Task Update_endpoint_after_database_conflict_recovers_same_service_scope()
    {
        using var factory = new RasHubWebApplicationFactory();
        var gate = await factory.SeedRasGateAsync();
        var endpoint = await factory.SeedRasEndpointAsync(gate.Id);
        using var serviceScope = factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider
            .GetRequiredService<RasHubDbContext>();
        _ = await db.RasEndpoints.SingleAsync(
            item => item.Id == endpoint.Id,
            TestContext.Current.CancellationToken);

        using (var concurrentScope = factory.Services.CreateScope())
        {
            var concurrentDb = concurrentScope.ServiceProvider
                .GetRequiredService<RasHubDbContext>();
            var concurrentEndpoint = await concurrentDb.RasEndpoints
                .SingleAsync(
                    item => item.Id == endpoint.Id,
                    TestContext.Current.CancellationToken);
            concurrentEndpoint.Name = "Concurrent RAS";
            await concurrentDb.SaveChangesAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal(2, concurrentEndpoint.ConfigurationRevision);
        }

        var service = CreateEndpointService(serviceScope.ServiceProvider);
        var staleResult = await service.UpdateAsync(
            endpoint.Id,
            1,
            new RasEndpointEditorValues(
                "Stale RAS",
                gate.Id,
                "ras.example.test",
                1545,
                true),
            TestContext.Current.CancellationToken);

        Assert.False(staleResult.Succeeded);
        Assert.Empty(db.ChangeTracker.Entries());

        var recoveredResult = await service.UpdateAsync(
            endpoint.Id,
            2,
            new RasEndpointEditorValues(
                "Recovered RAS",
                gate.Id,
                "ras.example.test",
                1545,
                true),
            TestContext.Current.CancellationToken);

        Assert.True(recoveredResult.Succeeded);
        var stored = await factory.FindRasEndpointAsync(endpoint.Id);
        Assert.NotNull(stored);
        Assert.Equal("Recovered RAS", stored.Name);
        Assert.Equal(3, stored.ConfigurationRevision);
    }

    private static RasGateAdministrationService CreateGateService(
        IServiceProvider services)
    {
        return new RasGateAdministrationService(
            services.GetRequiredService<RasHubDbContext>(),
            services.GetRequiredService<RasGateQueries>(),
            services.GetRequiredService<RasGateRegistry>(),
            services.GetRequiredService<IRasGateEndpointFactory>(),
            services.GetRequiredService<InteractiveTaskRunner>(),
            CreateAuthenticationStateProvider(),
            services.GetRequiredService<IAuthorizationService>(),
            services.GetRequiredService<ILogger<RasGateAdministrationService>>());
    }

    private static RasEndpointAdministrationService CreateEndpointService(
        IServiceProvider services)
    {
        return new RasEndpointAdministrationService(
            services.GetRequiredService<RasHubDbContext>(),
            services.GetRequiredService<RasEndpointQueries>(),
            services.GetRequiredService<RasGateQueries>(),
            services.GetRequiredService<RasEndpointRegistry>(),
            CreateAuthenticationStateProvider(),
            services.GetRequiredService<IAuthorizationService>(),
            services.GetRequiredService<ILogger<RasEndpointAdministrationService>>());
    }

    private static AuthenticationStateProvider
        CreateAuthenticationStateProvider()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, AppRoles.Admin)],
            "Test");
        return new StaticAuthenticationStateProvider(
            new ClaimsPrincipal(identity));
    }

    private sealed class StaticAuthenticationStateProvider(
        ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(new AuthenticationState(principal));
        }
    }
}
