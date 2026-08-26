using System.Net;
using RasHub.Contracts.Common;
using RasHub.Web.Api;

namespace RasHub.Web.Middlewares;

public static class ApiStatusCodePagesExtensions
{
    private static readonly PathString ApiPath = new("/api");

    public static void UseApiStatusCodePages(this IApplicationBuilder app)
    {
        app.UseStatusCodePages(async statusCodeContext =>
        {
            var context = statusCodeContext.HttpContext;

            if (!context.Request.Path.StartsWithSegments(ApiPath))
                return;

            var statusCode = (HttpStatusCode)context.Response.StatusCode;
            var response = ApiResponse<object>.Fail(statusCode);

            context.Response.Headers[ApiTrace.HeaderName] = ApiTrace.GetTraceId(context);

            await context.Response.WriteAsJsonAsync(
                response,
                context.RequestAborted);
        });
    }
}
