using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RasHub.Contracts.Common;

namespace RasHub.Web.Api.Filters;

public sealed class ApiResponseResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        switch (context.Result)
        {
            case ObjectResult objectResult:
                SetTraceId(context);
                NormalizeObjectResult(objectResult);
                break;

            case StatusCodeResult statusCodeResult:
                SetTraceId(context);

                if (statusCodeResult.StatusCode != StatusCodes.Status204NoContent)
                    context.Result = new ObjectResult(
                        CreateResponse(statusCodeResult.StatusCode, null))
                    { StatusCode = statusCodeResult.StatusCode };

                break;
        }

        await next();
    }

    private static void NormalizeObjectResult(ObjectResult objectResult)
    {
        if (objectResult.Value is IApiResponse response)
        {
            objectResult.DeclaredType = response.GetType();
            objectResult.StatusCode = (int)response.StatusCode;
            return;
        }

        var statusCode = objectResult.StatusCode ?? StatusCodes.Status200OK;

        if (statusCode == StatusCodes.Status204NoContent)
            return;

        var normalizedResponse = CreateResponse(statusCode, objectResult.Value);

        objectResult.Value = normalizedResponse;
        objectResult.DeclaredType = normalizedResponse.GetType();
        objectResult.StatusCode = statusCode;
    }

    private static IApiResponse CreateResponse(int statusCode, object? value)
    {
        if (statusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices)
            return ApiResponse<object>.Ok(value);

        var httpStatusCode = (HttpStatusCode)statusCode;

        return value switch
        {
            ApiError error => ApiResponse<object>.Fail(httpStatusCode, error),
            IEnumerable<ApiError> errors => ApiResponse<object>.Fail(httpStatusCode, errors),
            _ => ApiResponse<object>.Fail(httpStatusCode)
        };
    }

    private static void SetTraceId(ResultExecutingContext context)
    {
        context.HttpContext.Response.Headers[ApiTrace.HeaderName] =
            ApiTrace.GetTraceId(context.HttpContext);
    }
}
