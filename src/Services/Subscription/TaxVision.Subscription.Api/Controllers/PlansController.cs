using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Modules.Commands;
using TaxVision.Subscription.Application.Modules.Dtos;
using TaxVision.Subscription.Application.Plans.Commands;
using TaxVision.Subscription.Application.Plans.Dtos;
using TaxVision.Subscription.Application.Plans.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("api/plans")]
public sealed class PlansController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<List<PlanDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool? isActive, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<List<PlanDto>>(new GetAllPlansQuery(isActive), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PlanDto>>(new GetPlanByIdQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<PlanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePlanRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PlanDto>>(new CreatePlanCommand(
            Name:                   request.Name,
            Title:                  request.Title,
            Description:            request.Description,
            BasePriceMonthly:       request.BasePriceMonthly,
            BasePriceAnnual:        request.BasePriceAnnual,
            PricePerAdditionalSeat: request.PricePerAdditionalSeat,
            IncludedSeats:          request.IncludedSeats,
            Currency:               request.Currency,
            IsActive:               request.IsActive,
            ServiceLevel:           request.ServiceLevel,
            Features:               request.Features), ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePlanRequest request, CancellationToken ct)
    {
        if (id != request.Id)
            return BadRequest(new Error("Plan.IdMismatch", "Route ID does not match body ID."));

        var result = await bus.InvokeAsync<Result<PlanDto>>(new UpdatePlanCommand(
            Id:                     request.Id,
            Name:                   request.Name,
            Title:                  request.Title,
            Description:            request.Description,
            BasePriceMonthly:       request.BasePriceMonthly,
            BasePriceAnnual:        request.BasePriceAnnual,
            PricePerAdditionalSeat: request.PricePerAdditionalSeat,
            IncludedSeats:          request.IncludedSeats,
            IsActive:               request.IsActive,
            ServiceLevel:           request.ServiceLevel,
            Features:               request.Features), ct);

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
        var result = await bus.InvokeAsync<Result>(new DeletePlanCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleStatus(
        Guid id,
        [FromBody] TogglePlanStatusRequest request,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PlanDto>>(
            new TogglePlanStatusCommand(id, request.IsActive), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/modules/{moduleId:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<ModuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignModule(Guid id, Guid moduleId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ModuleDto>>(
            new AssignModuleToPlanCommand(moduleId, id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}/modules/{moduleId:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnassignModule(Guid id, Guid moduleId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(
            new UnassignModuleFromPlanCommand(moduleId, id), ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
