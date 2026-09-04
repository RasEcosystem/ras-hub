using System.Globalization;
using System.Threading.RateLimiting;

namespace RasHub.Web.Infrastructure.Security;

public static class AuthenticationRateLimitingServiceCollectionExtensions
{
    private const int PermitLimit = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static readonly string[] ProtectedPaths =
    [
        "/Account/Login",
        "/Account/LoginWith2fa",
        "/Account/LoginWithRecoveryCode"
    ];

    public static IServiceCollection AddAuthenticationRateLimiting(
        this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math
                        .Ceiling(retryAfter.TotalSeconds)
                        .ToString(CultureInfo.InvariantCulture);

                context.HttpContext.Response.ContentType = "text/plain";
                await context.HttpContext.Response.WriteAsync(
                    "Too many authentication attempts. Try again later.",
                    cancellationToken);
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                IsProtectedRequest(context)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        GetPartitionKey(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PermitLimit,
                            Window = Window,
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            AutoReplenishment = true
                        })
                    : RateLimitPartition.GetNoLimiter("unrestricted"));
        });

        return services;
    }

    private static bool IsProtectedRequest(HttpContext context)
    {
        return HttpMethods.IsPost(context.Request.Method) &&
               GetCanonicalProtectedPath(context.Request.Path) is not null;
    }

    private static string GetPartitionKey(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ??
                            "unknown";

        var canonicalPath = GetCanonicalProtectedPath(context.Request.Path) ??
                            throw new InvalidOperationException(
                                "A rate-limit key was requested for an unprotected path.");

        return $"{canonicalPath}:{remoteAddress}";
    }

    private static string? GetCanonicalProtectedPath(PathString path)
    {
        var value = path.Value?.TrimEnd('/');

        if (string.IsNullOrEmpty(value))
            return null;

        return ProtectedPaths.FirstOrDefault(protectedPath =>
            string.Equals(protectedPath, value, StringComparison.OrdinalIgnoreCase));
    }
}
