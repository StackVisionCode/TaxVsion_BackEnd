using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Modules.Commands;
using TaxVision.Subscription.Application.Modules.Dtos;
using TaxVision.Subscription.Application.Modules.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("api/modules")]
public sealed class ModulesController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<List<ModuleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? isActive,
        [FromQuery] Guid? planId,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<List<ModuleDto>>(new GetAllModulesQuery(isActive, planId), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<ModuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ModuleDto>>(new GetModuleByIdQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<ModuleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateModuleRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ModuleDto>>(
            new CreateModuleCommand(request.Name, request.Description, request.Url, request.IsActive), ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<ModuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateModuleRequest request, CancellationToken ct)
    {
        if (id != request.Id)
            return BadRequest(new Error("Module.IdMismatch", "Route ID does not match body ID."));

        var result = await bus.InvokeAsync<Result<ModuleDto>>(
            new UpdateModuleCommand(request.Id, request.Name, request.Description, request.Url, request.IsActive), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new DeleteModuleCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<ModuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleStatus(
        Guid id,
        [FromBody] ToggleModuleStatusRequest request,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ModuleDto>>(
            new ToggleModuleStatusCommand(id, request.IsActive), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
