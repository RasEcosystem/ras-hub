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

        var authenticationRows = await (
                from candidate in _dbContext.Users.AsNoTracking()
                join userRole in _dbContext.UserRoles.AsNoTracking()
                    on candidate.Id equals userRole.UserId into userRoles
                from userRole in userRoles.DefaultIfEmpty()
                join role in _dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id into roles
                from role in roles.DefaultIfEmpty()
                where candidate.ApiKey == providedApiKey && !candidate.IsBlocked
                select new
                {
                    candidate.Id,
                    candidate.UserName,
                    candidate.Email,
                    RoleName = role == null ? null : role.Name
                })
            .ToListAsync(Context.RequestAborted);

        if (authenticationRows.Count == 0)
            return AuthenticateResult.Fail("The provided API key is invalid.");

        var authenticatedUser = authenticationRows[0];
        var roleClaims = authenticationRows
            .Where(row => row.RoleName is not null)
            .Select(row => new Claim(ClaimTypes.Role, row.RoleName!));

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, authenticatedUser.Id), new Claim(
                    ClaimTypes.Name,
                    authenticatedUser.UserName ??
                    authenticatedUser.Email ??
                    authenticatedUser.Id)
            }.Concat(roleClaims),
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

    protected override async Task HandleForbiddenAsync(
        AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;

        var traceId = ApiTrace.GetTraceId(Context);
        Response.Headers[ApiTrace.HeaderName] = traceId;

        var response = ApiResponse<object>.Fail(HttpStatusCode.Forbidden);

        await Response.WriteAsJsonAsync(
            response,
            Context.RequestAborted);
    }
}
