using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Plans.Commands;
using TaxVision.Subscription.Application.Plans.Queries;
using TaxVision.Subscription.Application.Modules.Commands;
using TaxVision.Subscription.Application.Plans.Dtos;
using TaxVision.Subscription.Application.Modules.Dtos;
using TaxVision.Subscription.Domain.Plans;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("api/plans")]
public sealed class PlansController(IMessageBus bus) : ControllerBase
{
    /// <summary>Get all plans, optionally filtered by active status. PUBLIC.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll([FromQuery] bool? isActive, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<List<PlanDto>>(new GetAllPlansQuery(isActive), ct);
        return Ok(result);
    }

    /// <summary>Get a single plan by ID. PUBLIC.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<PlanDto>(new GetPlanByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Create a new plan (Developer only).</summary>
    [HttpPost]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> Create([FromBody] CreatePlanRequest request, CancellationToken ct)
    {
        var cmd = new CreatePlanCommand(
            Name: request.Name,
            Title: request.Title,
            Description: request.Description,
            BasePriceMonthly: request.BasePriceMonthly,
            BasePriceAnnual: request.BasePriceAnnual,
            PricePerAdditionalSeat: request.PricePerAdditionalSeat,
            IncludedSeats: request.IncludedSeats,
            Currency: request.Currency,
            IsActive: request.IsActive,
            ServiceLevel: request.ServiceLevel,
            Features: request.Features);

        var result = await bus.InvokeAsync<PlanDto>(cmd, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>Update an existing plan (Developer only).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePlanRequest request, CancellationToken ct)
    {
        if (id != request.Id)
            return BadRequest("Route ID does not match body ID.");

        var cmd = new UpdatePlanCommand(
            Id: request.Id,
            Name: request.Name,
            Title: request.Title,
            Description: request.Description,
            BasePriceMonthly: request.BasePriceMonthly,
            BasePriceAnnual: request.BasePriceAnnual,
            PricePerAdditionalSeat: request.PricePerAdditionalSeat,
            IncludedSeats: request.IncludedSeats,
            IsActive: request.IsActive,
            ServiceLevel: request.ServiceLevel,
            Features: request.Features);

        var result = await bus.InvokeAsync<PlanDto>(cmd, ct);
        return Ok(result);
    }

    /// <summary>Delete a plan (Developer only). Soft-deletes if subscriptions exist.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await bus.InvokeAsync<bool>(new DeletePlanCommand(id), ct);
        return NoContent();
    }

    /// <summary>Toggle plan active status (Developer only).</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> ToggleStatus(Guid id, [FromBody] ToggleStatusRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<PlanDto>(new TogglePlanStatusCommand(id, request.IsActive), ct);
        return Ok(result);
    }

    /// <summary>Assign a module to a plan (Developer only).</summary>
    [HttpPost("{id:guid}/modules/{moduleId:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> AssignModule(Guid id, Guid moduleId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<ModuleDto>(
            new AssignModuleToPlanCommand(moduleId, id), ct);
        return Ok(result);
    }

    /// <summary>Unassign a module from a plan (Developer only).</summary>
    [HttpDelete("{id:guid}/modules/{moduleId:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> UnassignModule(Guid id, Guid moduleId, CancellationToken ct)
    {
        await bus.InvokeAsync<bool>(
            new UnassignModuleFromPlanCommand(moduleId, id), ct);
        return NoContent();
    }

    public sealed record ToggleStatusRequest(bool IsActive);
}
