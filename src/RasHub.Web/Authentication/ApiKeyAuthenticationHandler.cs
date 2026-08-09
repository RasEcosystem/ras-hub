using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RasHub.Contracts.Common;
using RasHub.Web.Api;
using RasHub.Web.Data;

namespace RasHub.Web.Authentication;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ApplicationDbContext _dbContext;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApplicationDbContext dbContext)
        : base(options, logger, encoder)
    {
        _dbContext = dbContext;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var headerValues))
            return AuthenticateResult.NoResult();

        if (headerValues.Count != 1)
            return AuthenticateResult.Fail("A single API key must be provided.");

        var providedApiKey = headerValues.ToString();

        if (string.IsNullOrWhiteSpace(providedApiKey) ||
            providedApiKey.Length > ApplicationUser.ApiKeyMaxLength)
            return AuthenticateResult.Fail("The provided API key is invalid.");

        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(item => item.ApiKey == providedApiKey && !item.IsBlocked)
            .Select(item => new { item.Id, item.UserName, item.Email })
            .SingleOrDefaultAsync(Context.RequestAborted);

        if (user is null)
            return AuthenticateResult.Fail("The provided API key is invalid.");

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id)
            ],
            Scheme.Name);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;

        var traceId = ApiTrace.GetTraceId(Context);

        Response.Headers[ApiTrace.HeaderName] = traceId;

        var response = ApiResponse<object>.Fail(HttpStatusCode.Unauthorized);

        await Response.WriteAsJsonAsync(
            response,
            Context.RequestAborted);
    }
}