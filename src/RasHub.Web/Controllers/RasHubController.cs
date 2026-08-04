using Microsoft.AspNetCore.Mvc;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Responses;

namespace RasHub.Web.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class RasHubController : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<RasHubStatusResponse>), StatusCodes.Status200OK)]
    public ApiResponse<RasHubStatusResponse> GetStatus()
    {
        return ApiResponse<RasHubStatusResponse>.Ok(new RasHubStatusResponse
        {
            Version = ThisAssembly.AssemblyInformationalVersion
        });
    }
}