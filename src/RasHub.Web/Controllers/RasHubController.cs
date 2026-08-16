using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Responses;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Authentication;

namespace RasHub.Web.Controllers;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-hub")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[ControllerDescription(
    "Inspect the running service.")]
public sealed class RasHubController : ControllerBase
{
    [HttpGet("status")]
    [EndpointSummary("Get status")]
    [EndpointDescription("Returns the running application version.")]
    [ProducesResponseType(typeof(ApiResponse<RasHubStatusResponse>), StatusCodes.Status200OK)]
    public ApiResponse<RasHubStatusResponse> GetStatus()
    {
        return ApiResponse<RasHubStatusResponse>.Ok(new RasHubStatusResponse
        {
            Version = ThisAssembly.AssemblyInformationalVersion
        });
    }
}