using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Modules.Commands;
using TaxVision.Subscription.Application.Modules.Queries;
using TaxVision.Subscription.Application.Modules.Dtos;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("api/modules")]
public sealed class ModulesController(IMessageBus bus) : ControllerBase
{
    /// <summary>Get all modules, optionally filtered by active status or plan. PUBLIC.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll([FromQuery] bool? isActive, [FromQuery] Guid? planId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<List<ModuleDto>>(new GetAllModulesQuery(isActive, planId), ct);
        return Ok(result);
    }

    /// <summary>Get a single module by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<ModuleDto>(new GetModuleByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Create a new module (Developer only).</summary>
    [HttpPost]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateModuleRequest request, CancellationToken ct)
    {
        var cmd = new CreateModuleCommand(request.Name, request.Description, request.Url, request.IsActive);
        var result = await bus.InvokeAsync<ModuleDto>(cmd, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>Update a module (Developer only).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateModuleRequest request, CancellationToken ct)
    {
        if (id != request.Id) return BadRequest("Route ID does not match body ID.");
        var cmd = new UpdateModuleCommand(request.Id, request.Name, request.Description, request.Url, request.IsActive);
        var result = await bus.InvokeAsync<ModuleDto>(cmd, ct);
        return Ok(result);
    }

    /// <summary>Delete a module (Developer only). Soft-deletes if in use.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await bus.InvokeAsync<bool>(new DeleteModuleCommand(id), ct);
        return NoContent();
    }

    /// <summary>Toggle module active status (Developer only).</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> ToggleStatus(Guid id, [FromBody] ToggleStatusRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<ModuleDto>(new ToggleModuleStatusCommand(id, request.IsActive), ct);
        return Ok(result);
    }

    public sealed record ToggleStatusRequest(bool IsActive);
}
