using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Application.Tenants.Queries;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

[ApiController]
[Route("tenants")]
public sealed class TenantController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateTenantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateTenantCommand command, CancellationToken cancellationToken)
    {
        Result<CreateTenantResponse>? result = await bus.InvokeAsync<Result<CreateTenantResponse>>(
            command,
            cancellationToken
        );

        return result.IsSuccess
            ? Created($"/tenants/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<IReadOnlyList<TenantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TenantResponse>>> Get(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken cancellationToken = default
    )
    {
        if (page < 1 || size is < 1 or > 100)
        {
            return BadRequest(
                new Error("Tenant.Pagination", "Page must be at least 1 and size must be between 1 and 100.")
            );
        }

        var tenants = await bus.InvokeAsync<IReadOnlyList<TenantResponse>>(
            new GetTenantsQuery(page, size),
            cancellationToken
        );

        return Ok(tenants);
    }

    [HttpPatch("{tenantId:guid}/status")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangeStatus(
        Guid tenantId,
        [FromBody] ChangeTenantStatusRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await bus.InvokeAsync<Result>(
            new ChangeTenantStatusCommand(tenantId, request.Status),
            cancellationToken
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record ChangeTenantStatusRequest(EnumTenantStatus.TenantStatus Status);
