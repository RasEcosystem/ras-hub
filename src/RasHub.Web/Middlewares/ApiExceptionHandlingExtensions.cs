using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using RasHub.Contracts.Common;
using RasHub.Web.Api;

namespace RasHub.Web.Middlewares;

public static class ApiExceptionHandlingExtensions
{
    private static readonly PathString ApiPath = new("/api");

    public static void UseApiExceptionHandling(this IApplicationBuilder app)
    {
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(ApiPath),
            branch => branch.UseExceptionHandler(builder =>
            {
                builder.Run(async context =>
                {
                    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

                    if (exception is null)
                        throw new InvalidOperationException(
                            "Exception handler invoked without exception.");

                    context.Response.ContentType = "application/json";

                    var response = exception switch
                    {
                        ArgumentException argumentException => ApiResponse<object>.Fail(
                            HttpStatusCode.BadRequest,
                            new ApiError("bad_request", argumentException.Message)),
                        _ => ApiResponse<object>.Fail(HttpStatusCode.InternalServerError)
                    };

                    var traceId = ApiTrace.GetTraceId(context);

                    context.Response.Headers[ApiTrace.HeaderName] = traceId;

                    var logger = context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ApiExceptionHandling");

                    if (response.StatusCode >= HttpStatusCode.InternalServerError)
                        logger.LogError(
                            exception,
                            "Unhandled exception. TraceId: {TraceId}",
                            traceId);
                    else
                        logger.LogWarning(
                            "Request failed with {ExceptionType}: {Message}. TraceId: {TraceId}",
                            exception.GetType().Name,
                            exception.Message,
                            traceId);

                    context.Response.StatusCode = (int)response.StatusCode;

                    await context.Response.WriteAsJsonAsync(
                        response,
                        context.RequestAborted);
                });
            }));
    }
}