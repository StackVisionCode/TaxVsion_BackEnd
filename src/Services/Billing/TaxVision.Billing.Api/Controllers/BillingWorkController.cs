using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Billing.Api.Authorization;
using TaxVision.Billing.Application.Invoices.WorkTitles;
using Wolverine;

namespace TaxVision.Billing.Api.Controllers;

/// <summary>
/// Proveedor de títulos en vivo del historial (Work Profile): "Invoice {número}" de las facturas que quien mira
/// puede leer, con el permiso y el ABAC de la lectura de facturas. Tenant y actor del JWT.
/// </summary>
[ApiController]
[Route("billing/work")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class BillingWorkController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    // ---------- POST /billing/work/titles ----------
    [HttpPost("titles")]
    [RateLimit("billing.f.invoice_read")]
    [HasPermission(InvoicingPermissions.View)]
    [ProducesResponseType<WorkTitlesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Titles([FromBody] WorkTitlesRequest? request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Unauthorized();

        var ids = ParseTitleRefs(request);
        if (ids.IsFailure)
            return StatusCode(StatusCodes.Status400BadRequest, ids.Error);
        if (ids.Value.Count == 0)
            return Ok(new WorkTitlesResponse([]));

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var titles = await bus.InvokeAsync<WorkTitlesResponse>(
            new GetInvoiceWorkTitlesQuery(tenantId, actorId, canViewAll, ids.Value),
            ct
        );
        return Ok(titles);
    }

    // Ids de facturas sin repetidos; los tipos ajenos se ignoran. Más de WorkLimits.TitleBatch refs → error.
    private static Result<IReadOnlyList<Guid>> ParseTitleRefs(WorkTitlesRequest? request)
    {
        if (request?.Items is not { } refs)
            return Result.Failure<IReadOnlyList<Guid>>(new Error("Billing.Work.Titles", "items is required."));
        if (refs.Count > WorkLimits.TitleBatch)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("Billing.Work.Titles", $"items must have at most {WorkLimits.TitleBatch} refs.")
            );

        IReadOnlyList<Guid> ids = refs.Where(item =>
                item is not null
                && string.Equals(item.ResourceType, WorkResourceTypes.Invoice, StringComparison.Ordinal)
            )
            .Select(item => item.Id)
            .Distinct()
            .ToList();
        return Result.Success(ids);
    }
}
