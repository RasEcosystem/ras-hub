using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasHub.Application.Interfaces;
using RasHub.Contracts.Common;
using RasHub.Contracts.Common.Pagination;
using RasHub.Contracts.RasHub.Models;
using RasHub.Contracts.RasHub.Requests;
using RasHub.Domain;
using RasHub.Infrastructure.Database.Queries;

namespace RasHub.Web.Controllers;

[ApiController]
[Route("api/v1/ras-gates")]
[Authorize]
public sealed class RasGatesController : ControllerBase
{
    [HttpPost("get-paged")]
    [ProducesResponseType(
        typeof(ApiResponse<PageResult<RasGateModel>>),
        StatusCodes.Status200OK)]
    public async Task<ApiResponse<PageResult<RasGateModel>>> GetPaged(
        [FromBody] PageRequest request,
        [FromServices] RasGateQueries query,
        CancellationToken cancellationToken)
    {
        var result = await query.GetPagedAsync(
            request,
            cancellationToken);

        return ApiResponse<PageResult<RasGateModel>>.Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status404NotFound)]
    public async Task<ApiResponse<RasGateModel>> GetById(
        Guid id,
        [FromServices] RasGateQueries query,
        CancellationToken cancellationToken)
    {
        var rasGate = await query.GetByIdAsync(
            id,
            cancellationToken);

        if (rasGate is null)
            return CreateNotFoundResponse(id);

        return ApiResponse<RasGateModel>.Ok(rasGate);
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<RasGateModel>> Create(
        [FromBody] CreateRasGateRequest request,
        [FromServices] IRepository<RasGate> repository,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var rasGate = new RasGate
        {
            Name = request.Name,
            Url = request.Url,
            Port = request.Port,
            ApiKey = request.ApiKey
        };

        await repository.AddAsync(rasGate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var model = ToModel(rasGate);
        var location = Url.ActionLink(nameof(GetById), values: new { id = rasGate.Id });

        if (location is not null)
            Response.Headers.Location = location;

        return ApiResponse<RasGateModel>.Created(model);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status404NotFound)]
    public async Task<ApiResponse<RasGateModel>> Update(
        Guid id,
        [FromBody] UpdateRasGateRequest request,
        [FromServices] IRepository<RasGate> repository,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdAsync(id, cancellationToken);

        if (rasGate is null)
            return CreateNotFoundResponse(id);

        rasGate.Name = request.Name;
        rasGate.Url = request.Url;
        rasGate.Port = request.Port;

        if (request.ApiKey is not null)
            rasGate.ApiKey = request.ApiKey;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ApiResponse<RasGateModel>.Ok(ToModel(rasGate));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateModel>),
        StatusCodes.Status404NotFound)]
    public async Task<ApiResponse<RasGateModel>> Delete(
        Guid id,
        [FromServices] IRepository<RasGate> repository,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var rasGate = await repository.GetByIdAsync(id, cancellationToken);

        if (rasGate is null)
            return CreateNotFoundResponse(id);

        repository.Remove(rasGate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ApiResponse<RasGateModel>.Ok(ToModel(rasGate));
    }

    private static ApiResponse<RasGateModel> CreateNotFoundResponse(Guid id)
    {
        return ApiResponse<RasGateModel>.Fail(
            HttpStatusCode.NotFound,
            "ras_gate_not_found",
            $"RasGate '{id}' was not found.");
    }

    private static RasGateModel ToModel(RasGate rasGate)
    {
        return new RasGateModel(
            rasGate.Id,
            rasGate.Name,
            rasGate.Url,
            rasGate.Port,
            rasGate.CreatedAt,
            rasGate.UpdatedAt);
    }
}