using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Application.Tenants.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

[ApiController]
[Route("tenants")]
public sealed class TenantController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTenantCommand command,
        CancellationToken cancellationToken)
    {
        Result<TenantResponse>? result = await bus.InvokeAsync<Result<TenantResponse>>(
            command,
            cancellationToken);

        return result.IsSuccess
            ? Created($"/tenants/{result.Value.Id}", result.Value)
            : BadRequest(result.Error);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TenantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TenantResponse>>> Get(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken cancellationToken = default)
    {
        var tenants = await bus.InvokeAsync<IReadOnlyList<TenantResponse>>(
            new GetTenantsQuery(page, size),
            cancellationToken);

        return Ok(tenants);
    }
}
