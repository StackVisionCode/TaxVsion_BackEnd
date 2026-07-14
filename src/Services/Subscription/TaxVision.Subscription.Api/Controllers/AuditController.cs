using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Audit.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("audit")]
[Authorize(Roles = "TenantAdmin,PlatformAdmin")]
public sealed class AuditController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? aggregateType, [FromQuery] Guid? aggregateId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<AuditLogEntryResponse>>>(
            new GetAuditLogsQuery(tenantId, aggregateType, aggregateId, from, to, page, pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
