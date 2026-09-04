using System.Net;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;

namespace RasHub.Web.Api.RasEndpoints;

internal static class RasEndpointApiResponses
{
    public static ApiResponse<T> EndpointNotFound<T>(Guid rasEndpointId)
    {
        return ApiResponse<T>.Fail(
            HttpStatusCode.NotFound,
            "ras_endpoint_not_found",
            $"RAS endpoint '{rasEndpointId}' was not found.");
    }

    public static ApiResponse<RasEndpointModel> InvalidAddress()
    {
        return ApiResponse<RasEndpointModel>.Fail(
            HttpStatusCode.BadRequest,
            "ras_endpoint_address_invalid",
            "The RAS endpoint address is invalid.");
    }

    public static ApiResponse<RasEndpointModel> ConcurrentUpdate()
    {
        return ApiResponse<RasEndpointModel>.Fail(
            HttpStatusCode.Conflict,
            "ras_endpoint_concurrency_conflict",
            "RAS endpoint configuration changed concurrently. Retry with current data.");
    }
}
