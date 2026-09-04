using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RasHub.Application.RasEndpoints.Exceptions;
using RasHub.Application.RasEndpoints.Models;
using RasHub.Application.RasEndpoints.Services;
using RasHub.Contracts.Common;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Domain;
using RasHub.Web.Api.OpenApi;
using RasHub.Web.Api.RasEndpoints;
using RasHub.Web.Authentication;
using RasHub.Web.Infrastructure.Authorization;

namespace RasHub.Web.Controllers.RasEndpoints;

[ApiController]
[ProducesErrorResponseType(typeof(OpenApiErrorResponse))]
[Route("api/v1/ras-endpoints")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[Tags("RasEndpoints")]
[ControllerDescription("RasEndpoints",
    "Inspect and manage Hub-owned RAS administration endpoints.")]
public sealed class RasEndpointAdministrationController(
    RasEndpointRegistry rasEndpointRegistry) : ControllerBase
{
    [HttpPost(Name = "RegisterRasEndpoint")]
    [Authorize(Policy = AppPolicies.ManageRasEndpoints)]
    [EndpointSummary("Register RAS endpoint")]
    [EndpointDescription(
        "Registers a RAS administration endpoint in the Hub and returns its public details.")]
    [ProducesResponseType<ApiResponse<RasEndpointModel>>(
        StatusCodes.Status201Created)]
    [ProducesApiErrors(StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<RasEndpointModel>> Register(
        [FromBody] CreateRasEndpointRequest request,
        CancellationToken cancellationToken)
    {
        RasEndpoint rasEndpoint;
        try
        {
            rasEndpoint = await rasEndpointRegistry.RegisterAsync(
                new RasEndpointRegistration(
                    request.Name,
                    request.Host,
                    request.Port,
                    request.IsActive),
                cancellationToken);
        }
        catch (RasEndpointAddressValidationException)
        {
            return RasEndpointApiResponses.InvalidAddress();
        }

        var model = ToModel(rasEndpoint);
        var location = Url.Link(
            "GetRasEndpoint",
            new { rasEndpointId = rasEndpoint.Id });

        if (location is not null)
            Response.Headers.Location = location;

        return ApiResponse<RasEndpointModel>.Created(model);
    }

    [HttpPut("{rasEndpointId:guid}", Name = "UpdateRasEndpoint")]
    [Authorize(Policy = AppPolicies.ManageRasEndpoints)]
    [EndpointSummary("Update RAS endpoint")]
    [EndpointDescription(
        "Replaces the RAS endpoint address and activity state at the expected configuration revision.")]
    [ProducesResponseType<ApiResponse<RasEndpointModel>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status400BadRequest,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<RasEndpointModel>> Update(
        Guid rasEndpointId,
        [FromBody] UpdateRasEndpointRequest request,
        CancellationToken cancellationToken)
    {
        RasEndpoint? rasEndpoint;
        try
        {
            rasEndpoint = await rasEndpointRegistry.UpdateAsync(
                rasEndpointId,
                new RasEndpointRegistrationUpdate(
                    request.Name,
                    request.Host,
                    request.Port,
                    request.IsActive,
                    request.ExpectedConfigurationRevision),
                cancellationToken);
        }
        catch (RasEndpointAddressValidationException)
        {
            return RasEndpointApiResponses.InvalidAddress();
        }
        catch (RasEndpointRevisionConflictException)
        {
            return RasEndpointApiResponses.ConcurrentUpdate();
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasEndpointApiResponses.ConcurrentUpdate();
        }

        if (rasEndpoint is null)
            return RasEndpointApiResponses.EndpointNotFound<RasEndpointModel>(
                rasEndpointId);

        return ApiResponse<RasEndpointModel>.Ok(ToModel(rasEndpoint));
    }

    [HttpDelete("{rasEndpointId:guid}", Name = "UnregisterRasEndpoint")]
    [Authorize(Policy = AppPolicies.ManageRasEndpoints)]
    [EndpointSummary("Unregister RAS endpoint")]
    [EndpointDescription(
        "Removes the RAS endpoint from regular queries while retaining its stored record.")]
    [ProducesResponseType<ApiResponse<RasEndpointModel>>(
        StatusCodes.Status200OK)]
    [ProducesApiErrors(
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict)]
    public async Task<ApiResponse<RasEndpointModel>> Unregister(
        Guid rasEndpointId,
        CancellationToken cancellationToken)
    {
        RasEndpoint? rasEndpoint;
        try
        {
            rasEndpoint = await rasEndpointRegistry.UnregisterAsync(
                rasEndpointId,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RasEndpointApiResponses.ConcurrentUpdate();
        }

        if (rasEndpoint is null)
            return RasEndpointApiResponses.EndpointNotFound<RasEndpointModel>(
                rasEndpointId);

        return ApiResponse<RasEndpointModel>.Ok(ToModel(rasEndpoint));
    }

    private static RasEndpointModel ToModel(RasEndpoint rasEndpoint)
    {
        return new RasEndpointModel
        {
            Id = rasEndpoint.Id,
            Name = rasEndpoint.Name,
            Host = rasEndpoint.Host,
            Port = rasEndpoint.Port,
            IsActive = rasEndpoint.IsActive,
            ConfigurationRevision = rasEndpoint.ConfigurationRevision,
            CreatedAt = rasEndpoint.CreatedAt,
            UpdatedAt = rasEndpoint.UpdatedAt
        };
    }
}
