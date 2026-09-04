using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.IntegrationTests.Authentication;

public sealed class AdministrationAuthorizationGuardTests
{
    [Theory]
    [InlineData("gate", AppPolicies.ManageRasGates)]
    [InlineData("endpoint", AppPolicies.ManageRasEndpoints)]
    [InlineData("settings", AppPolicies.ManageGlobalSettings)]
    public async Task Operation_uses_its_administration_policy(
        string operation,
        string expectedPolicy)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("Test"));
        var authorization = new RecordingAuthorizationService(true);
        var guard = new AdministrationAuthorizationGuard(
            new StaticAuthenticationStateProvider(principal),
            authorization);

        switch (operation)
        {
            case "gate":
                await guard.RequireRasGateManagementAsync();
                break;
            case "endpoint":
                await guard.RequireRasEndpointManagementAsync();
                break;
            case "settings":
                var authorizedPrincipal = await guard
                    .RequireGlobalSettingsManagementAsync();
                Assert.Same(principal, authorizedPrincipal);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        Assert.Equal(expectedPolicy, authorization.RequestedPolicy);
    }

    [Fact]
    public async Task Denied_gate_management_uses_existing_error_message()
    {
        var guard = new AdministrationAuthorizationGuard(
            new StaticAuthenticationStateProvider(
                new ClaimsPrincipal(new ClaimsIdentity("Test"))),
            new RecordingAuthorizationService(false));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            guard.RequireRasGateManagementAsync);

        Assert.Equal(
            "Administrator access is required to manage RasGates.",
            exception.Message);
    }

    private sealed class StaticAuthenticationStateProvider(
        ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(new AuthenticationState(principal));
        }
    }

    private sealed class RecordingAuthorizationService(bool succeeds)
        : IAuthorizationService
    {
        public string? RequestedPolicy { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            throw new NotSupportedException();
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            RequestedPolicy = policyName;
            return Task.FromResult(
                succeeds
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed());
        }
    }
}
