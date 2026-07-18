using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Postmaster.Api.Authorization;
using TaxVision.Postmaster.Api.Common;
using TaxVision.Postmaster.Api.Requests;
using TaxVision.Postmaster.Application.Suppression.Commands.AddSuppressionEntry;
using TaxVision.Postmaster.Application.Suppression.Commands.RemoveSuppressionEntry;
using TaxVision.Postmaster.Application.Suppression.Queries.ListSuppressionEntries;
using TaxVision.Postmaster.Domain.Suppression;
using Wolverine;

namespace TaxVision.Postmaster.Api.Controllers;

[ApiController]
[Route("postmaster/suppression")]
public sealed class SuppressionController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(PostmasterPermissions.SuppressionRead)]
    public async Task<IActionResult> List(
        [FromQuery] string? address,
        [FromQuery] SuppressionReason? reason,
        [FromQuery] int page,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var entries = await bus.InvokeAsync<IReadOnlyList<SuppressionListEntryDto>>(
            new ListSuppressionEntriesQuery(tenantId, address, reason, page, PageSize: 50),
            ct
        );
        return Ok(entries);
    }

    [HttpPost]
    [HasPermission(PostmasterPermissions.SuppressionWrite)]
    public async Task<IActionResult> Add([FromBody] AddSuppressionEntryRequest body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        User.TryGetUserId(out var actingUserId);
        var cmd = new AddSuppressionEntryCommand(tenantId, body.Address, body.Reason, actingUserId, body.Notes);
        var result = await bus.InvokeAsync<Result>(cmd, ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{address}")]
    [HasPermission(PostmasterPermissions.SuppressionWrite)]
    public async Task<IActionResult> Remove(string address, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(new RemoveSuppressionEntryCommand(tenantId, address), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
