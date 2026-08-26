using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Responses;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers.RasHub;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/info")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("RasHub")]
[ControllerDescription("RasHub",
    "Inspect the running service.")]
public sealed class RasHubController : ControllerBase
{
    [HttpGet(Name = "GetRasHubInfo")]
    [EndpointSummary("Get service information")]
    [EndpointDescription("Returns the running application version.")]
    [ProducesResponseType<ApiResponse<RasHubInfoResponse>>(StatusCodes.Status200OK)]
    public ApiResponse<RasHubInfoResponse> GetInfo()
    {
        return ApiResponse<RasHubInfoResponse>.Ok(new RasHubInfoResponse { Version = RasHubVersion.Informational });
    }
}
